using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Portfolio.Identity.Application;
using Portfolio.Identity.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;
using StackExchange.Redis;

namespace Portfolio.IntegrationTests.Caching;

/// <summary>
/// The Valkey composition (<c>AddValkey</c>) on its own, in a minimal service provider: the cache boundary, the
/// revocation blocklist and the connection, exactly as the application registers them, without the web host.
/// </summary>
internal sealed class CacheHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public CacheHost(
        string? connectionString = null,
        string? prefix = null,
        RevocationFailureMode failureMode = RevocationFailureMode.Deny,
        int operationTimeoutMilliseconds = 250
    )
    {
        Prefix = prefix ?? ValkeyFixture.NewPrefix();
        ConnectionString = connectionString ?? ValkeyFixture.Current.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionStrings:Valkey"] = ConnectionString,
                    ["Valkey:KeyPrefix"] = Prefix,
                    ["Valkey:RevocationCheckFailureMode"] = failureMode.ToString(),
                    ["Valkey:OperationTimeoutMilliseconds"] = operationTimeoutMilliseconds.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddProvider(Logs));
        services.AddMetrics();
        services.AddSingleton(TimeProvider.System);
        services.AddValkey(configuration);
        services.AddSingleton<IRevokedTokenStore, ValkeyRevokedTokenStore>();
        _provider = services.BuildServiceProvider();
    }

    public string Prefix { get; }

    public string ConnectionString { get; }

    /// <summary>The log records the composition wrote, to prove what is and is not logged.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    public IResilientCache Cache => _provider.GetRequiredService<IResilientCache>();

    public ValkeyConnection Connection => _provider.GetRequiredService<ValkeyConnection>();

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>The keys under this host's prefix, read straight from the server.</summary>
    public async Task<string[]> KeysAsync()
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(RawOptions());
        await using (multiplexer)
        {
            var server = multiplexer.GetServers()[0];
            return [.. server.Keys(pattern: $"{Prefix}:*").Select(key => key.ToString())];
        }
    }

    /// <summary>
    /// The stored value and the remaining TTL of a key, read straight from the server. A cache entry written by
    /// <c>HybridCache</c> is a hash (its payload is the <c>data</c> field); the blocklist writes plain strings.
    /// </summary>
    public async Task<(string? Value, TimeSpan? Ttl)> InspectAsync(string key)
    {
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(RawOptions());
        await using (multiplexer)
        {
            var database = multiplexer.GetDatabase();
            var value =
                await database.KeyTypeAsync(key) == RedisType.Hash
                    ? (string?)await database.HashGetAsync(key, "data")
                    : (string?)await database.StringGetAsync(key);
            return (value, await database.KeyTimeToLiveAsync(key));
        }
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private ConfigurationOptions RawOptions()
    {
        var options = ConfigurationOptions.Parse(ConnectionString);
        options.AllowAdmin = true;
        return options;
    }
}
