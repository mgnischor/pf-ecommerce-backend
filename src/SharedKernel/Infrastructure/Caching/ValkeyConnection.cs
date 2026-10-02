using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The one connection to Valkey (the client is wire-compatible), shared by the cache and the revocation blocklist
/// (ai/DATABASE.md §4.1). It never fails the application on start-up or while Valkey is down: the multiplexer is created
/// with <c>AbortOnConnectFail = false</c>, keeps retrying in the background, and every command has a short timeout.
/// </summary>
internal sealed class ValkeyConnection(IConfiguration configuration, IOptions<ValkeyOptions> options) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private Task<IConnectionMultiplexer>? _connection;

    /// <summary>Gets the shared multiplexer, connecting on first use and again after a failed attempt.</summary>
    public Task<IConnectionMultiplexer> GetAsync()
    {
        lock (_gate)
        {
            if (_connection is null || _connection.IsFaulted || _connection.IsCanceled)
            {
                var settings = BuildOptions(
                    configuration.GetConnectionString(ValkeyOptions.ConnectionStringName),
                    options.Value
                );
                _connection = ConnectAsync(settings);
            }

            return _connection;
        }
    }

    /// <summary>
    /// Whether the multiplexer is connected right now. A plain property read, no I/O: the cache boundary uses it to skip
    /// Valkey entirely during an outage instead of paying a timeout on every request.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _connection is { IsCompletedSuccessfully: true } && _connection.Result.IsConnected;
            }
        }
    }

    /// <summary>
    /// Whether Valkey can be used now. While the very first connection is still being established it waits for it, once,
    /// up to the connect timeout; after that an unreachable server answers <c>false</c> immediately, so an outage costs
    /// one timeout, not one per request. The client keeps reconnecting in the background either way.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return true;
        }

        var connecting = GetAsync();
        if (!connecting.IsCompleted)
        {
            try
            {
                await connecting.WaitAsync(
                    TimeSpan.FromMilliseconds(options.Value.ConnectTimeoutMilliseconds + 100),
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is TimeoutException or RedisException)
            {
                // Not reachable within the budget: the caller falls back to the source of truth.
            }
        }

        return IsConnected;
    }

    /// <summary>Builds the client settings: explicit timeouts, retrying reconnection, and a name for <c>CLIENT LIST</c>.</summary>
    /// <param name="connectionString">The secret connection string (<c>host:port,password=...</c>).</param>
    /// <param name="settings">Timeouts.</param>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    public static ConfigurationOptions BuildOptions(string? connectionString, ValkeyOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The message names the setting, never a value.
            throw new InvalidOperationException(
                $"The connection string '{ValkeyOptions.ConnectionStringName}' is not configured (set ConnectionStrings__Valkey)."
            );
        }

        var parsed = ConfigurationOptions.Parse(connectionString);
        parsed.AbortOnConnectFail = false;
        parsed.ConnectTimeout = settings.ConnectTimeoutMilliseconds;
        parsed.SyncTimeout = settings.OperationTimeoutMilliseconds;
        parsed.AsyncTimeout = settings.OperationTimeoutMilliseconds;
        parsed.ConnectRetry = 2;
        parsed.ReconnectRetryPolicy = new ExponentialRetry(
            deltaBackOffMilliseconds: 200,
            maxDeltaBackOffMilliseconds: 5_000
        );
        parsed.ClientName = "pf-ecommerce-api";
        return parsed;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task<IConnectionMultiplexer>? connection;
        lock (_gate)
        {
            connection = _connection;
            _connection = null;
        }

        if (connection is { IsCompletedSuccessfully: true })
        {
            await connection.Result.DisposeAsync();
        }
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(ConfigurationOptions settings) =>
        await ConnectionMultiplexer.ConnectAsync(settings);
}
