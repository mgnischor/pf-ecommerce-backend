using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Publishes the two outbox gauges of ai/OBSERVABILITY.md §5.5 for every context: how many events are waiting
/// (<c>app.outbox.pending</c>) and how old the oldest one is (<c>app.outbox.oldest_pending_age</c>), the freshness SLI of
/// integration events and the input of the "outbox stale" alert. Runs whether or not a relay is wired: until a publisher
/// exists the backlog only grows, which is exactly what must be visible. The gauges read the last sample, so a metric
/// collection never touches the database (an observable callback must be cheap and non-blocking); a failed sample keeps
/// the last value and is logged once per failure with the exception type only.
/// </summary>
internal sealed class OutboxBacklogMonitor : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;
    private readonly IEnumerable<MigratableContext> _contexts;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxBacklogMonitor> _logger;
    private readonly ConcurrentDictionary<string, (long Pending, double OldestAgeSeconds)> _samples = new(
        StringComparer.Ordinal
    );

    /// <summary>Creates the monitor and registers its gauges.</summary>
    /// <param name="scopes">Creates a scope per sample, so each reads through its own context.</param>
    /// <param name="contexts">The registered contexts.</param>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    /// <param name="timeProvider">Source of time.</param>
    /// <param name="logger">Logger.</param>
    public OutboxBacklogMonitor(
        IServiceScopeFactory scopes,
        IEnumerable<MigratableContext> contexts,
        IMeterFactory meterFactory,
        TimeProvider timeProvider,
        ILogger<OutboxBacklogMonitor> logger
    )
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _scopes = scopes;
        _contexts = contexts;
        _timeProvider = timeProvider;
        _logger = logger;

        var meter = meterFactory.Create(TelemetryNames.OutboxMeter);
        meter.CreateObservableGauge(
            "app.outbox.pending",
            () => Measurements(sample => (double)sample.Pending),
            unit: "{message}",
            description: "Domain events committed and not yet published."
        );
        meter.CreateObservableGauge(
            "app.outbox.oldest_pending_age",
            () => Measurements(sample => sample.OldestAgeSeconds),
            unit: "s",
            description: "Age of the oldest event not yet published; 0 when nothing is pending."
        );
    }

    /// <summary>Reads the backlog of every context once. Public so a test can drive it with a fake clock.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        foreach (var contextType in _contexts.Select(registration => registration.ContextType))
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var context = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType);

                var pending = context.OutboxMessages.AsNoTracking().Where(message => message.ProcessedAt == null);
                var count = await pending.LongCountAsync(cancellationToken);
                var oldest = await pending.MinAsync(message => (DateTimeOffset?)message.OccurredAt, cancellationToken);

                var age = oldest is { } at ? Math.Max(0, (_timeProvider.GetUtcNow() - at).TotalSeconds) : 0;
                _samples[context.Schema] = (count, age);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                TelemetryLog.OutboxSampleFailed(_logger, contextType.Name, exception.GetType().Name);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _timeProvider);
        do
        {
            await SampleAsync(stoppingToken);
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private IEnumerable<Measurement<double>> Measurements(
        Func<(long Pending, double OldestAgeSeconds), double> value
    ) =>
        _samples.Select(sample => new Measurement<double>(
            value(sample.Value),
            new KeyValuePair<string, object?>(TelemetryNames.BoundedContext, sample.Key)
        ));
}
