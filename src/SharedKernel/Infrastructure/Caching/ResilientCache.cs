using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// <see cref="IResilientCache"/> over <see cref="HybridCache"/> with Valkey as its shared (L2) store. Stampede protection
/// comes from <see cref="HybridCache"/>; the failure policy, the metrics and the span come from here. Telemetry follows
/// ai/OBSERVABILITY.md §5.5: requests by outcome (hit, miss, error), operation latency, and the count of errors swallowed
/// at this boundary. A cache error never sets an error status on the span: it is handled, normal flow.
/// </summary>
internal sealed partial class ResilientCache : IResilientCache
{
    // At most one log line per cache name in this window: an outage must not become a log flood. The counter
    // still counts every failure.
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(30);

    private static readonly ActivitySource Source = new(TelemetryNames.CacheSource);

    private readonly HybridCache _hybrid;
    private readonly ValkeyConnection _connection;
    private readonly ILogger<ResilientCache> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _prefix;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _duration;
    private readonly Dictionary<string, long> _lastLogged = new(StringComparer.Ordinal);

    /// <summary>Creates the cache boundary.</summary>
    /// <param name="hybrid">The hybrid cache (L1 optional, L2 Valkey).</param>
    /// <param name="connection">The shared connection, asked whether Valkey is reachable.</param>
    /// <param name="options">Valkey settings.</param>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    /// <param name="timeProvider">Source of time for the log throttle.</param>
    /// <param name="logger">Logger.</param>
    public ResilientCache(
        HybridCache hybrid,
        ValkeyConnection connection,
        IOptions<ValkeyOptions> options,
        IMeterFactory meterFactory,
        TimeProvider timeProvider,
        ILogger<ResilientCache> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _hybrid = hybrid;
        _connection = connection;
        _timeProvider = timeProvider;
        _logger = logger;
        _prefix = options.Value.KeyPrefix;

        var meter = meterFactory.Create(TelemetryNames.CacheMeter);
        _requests = meter.CreateCounter<long>(
            "app.cache.requests",
            unit: "{request}",
            description: "Cache lookups by outcome: hit, miss, or error (the value then came from the source of truth)."
        );
        _errors = meter.CreateCounter<long>(
            "app.cache.errors",
            unit: "{error}",
            description: "Cache failures swallowed at the cache boundary."
        );
        _duration = meter.CreateHistogram<double>(
            "app.cache.operation.duration",
            unit: "s",
            description: "Duration of a cache operation, including the fallback to the source of truth."
        );
    }

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        CacheEntry entry,
        string id,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(factory);

        var key = entry.KeyFor(_prefix, id);
        using var activity = StartActivity("get_or_create", entry);
        var started = Stopwatch.GetTimestamp();
        var factoryRan = false;
        var factoryFailed = false;

        if (!await _connection.IsAvailableAsync(cancellationToken))
        {
            // Valkey is down: answer from the source of truth at once instead of paying a timeout per request (the
            // client keeps reconnecting in the background). HybridCache would swallow the failure silently.
            Failed(entry, "get_or_create", "NotConnected");
            activity?.SetTag(TelemetryNames.CacheOutcome, "error");
            var direct = await factory(cancellationToken);
            Record(entry, "get_or_create", "error", started);
            return direct;
        }

        async ValueTask<T> Compute(CancellationToken token)
        {
            factoryRan = true;
            try
            {
                return await factory(token);
            }
            catch
            {
                factoryFailed = true;
                throw;
            }
        }

        try
        {
            var value = await _hybrid.GetOrCreateAsync(key, Compute, Options(entry), tags: null, cancellationToken);
            Record(entry, "get_or_create", factoryRan ? "miss" : "hit", started);
            return value;
        }
        catch (Exception exception)
            when (!factoryFailed
                && !cancellationToken.IsCancellationRequested
                && exception is not OperationCanceledException
            )
        {
            // The cache failed, not the source of truth: answer from the source, and say so in the signals.
            Failed(entry, "get_or_create", exception);
            activity?.SetTag(TelemetryNames.CacheOutcome, "error");
            var value = await factory(cancellationToken);
            Record(entry, "get_or_create", "error", started);
            return value;
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(CacheEntry entry, string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = entry.KeyFor(_prefix, id);
        using var activity = StartActivity("remove", entry);
        var started = Stopwatch.GetTimestamp();

        if (!await _connection.IsAvailableAsync(cancellationToken))
        {
            // Nothing is reachable to evict; the TTL bounds the stale value. Counted so the outage is visible.
            Failed(entry, "remove", "NotConnected");
            activity?.SetTag(TelemetryNames.CacheOutcome, "error");
            Record(entry, "remove", "error", started);
            return;
        }

        try
        {
            await _hybrid.RemoveAsync(key, cancellationToken);
            Record(entry, "remove", "hit", started);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            // The TTL bounds how long the stale value can live; the failure is counted and logged once in a while.
            Failed(entry, "remove", exception);
            activity?.SetTag(TelemetryNames.CacheOutcome, "error");
            Record(entry, "remove", "error", started);
        }
    }

    private static HybridCacheEntryOptions Options(CacheEntry entry) =>
        new()
        {
            Expiration = entry.Ttl,
            LocalCacheExpiration = entry.AllowLocalCopy ? entry.Ttl : null,
            Flags = entry.AllowLocalCopy ? HybridCacheEntryFlags.None : HybridCacheEntryFlags.DisableLocalCache,
        };

    private static Activity? StartActivity(string operation, CacheEntry entry)
    {
        var activity = Source.StartActivity($"cache {operation}", ActivityKind.Client);
        activity?.SetTag("db.system.name", "valkey");
        activity?.SetTag(TelemetryNames.CacheName, entry.Name);
        activity?.SetTag(TelemetryNames.CacheOperation, operation);
        return activity;
    }

    private void Record(CacheEntry entry, string operation, string outcome, long started)
    {
        var name = new KeyValuePair<string, object?>(TelemetryNames.CacheName, entry.Name);
        _requests.Add(1, name, new KeyValuePair<string, object?>(TelemetryNames.CacheOutcome, outcome));
        _duration.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            name,
            new KeyValuePair<string, object?>(TelemetryNames.CacheOperation, operation)
        );
    }

    private void Failed(CacheEntry entry, string operation, Exception exception) =>
        Failed(entry, operation, exception.GetType().Name);

    private void Failed(CacheEntry entry, string operation, string errorType)
    {
        _errors.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryNames.CacheName, entry.Name),
            new KeyValuePair<string, object?>(TelemetryNames.CacheOperation, operation)
        );

        var now = _timeProvider.GetTimestamp();
        lock (_lastLogged)
        {
            if (
                _lastLogged.TryGetValue(entry.Name, out var last)
                && _timeProvider.GetElapsedTime(last, now) < LogInterval
            )
            {
                return;
            }

            _lastLogged[entry.Name] = now;
        }

        // The exception type only: a provider message can carry the connection string or a key.
        CacheLog.OperationFailed(_logger, entry.Name, operation, errorType);
    }
}
