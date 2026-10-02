using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Composition of the Valkey access (ai/DATABASE.md §4): one shared connection, <c>HybridCache</c> with Valkey as its
/// shared store, the failure-proof cache boundary, and a readiness check that reports <em>degraded</em>, not down.
/// </summary>
internal static class ValkeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connection, the cache and the check. The connection string is the secret
    /// <c>ConnectionStrings:Valkey</c>; the host refuses to start without it, because token revocation depends on it.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddValkey(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<ValkeyOptions>, ValkeyOptionsValidator>();
        services
            .AddOptions<ValkeyOptions>()
            .Bind(configuration.GetSection(ValkeyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<ValkeyConnection>();

        // The distributed cache is the shared L2 store of HybridCache; it reuses the one multiplexer.
        services.AddStackExchangeRedisCache(_ => { });
        services
            .AddOptions<RedisCacheOptions>()
            .Configure<ValkeyConnection>(
                (redis, connection) => redis.ConnectionMultiplexerFactory = connection.GetAsync
            );

        services.AddHybridCache(options =>
        {
            // The per-entry TTL always wins; this is only the ceiling for an entry that forgot one.
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                LocalCacheExpiration = TimeSpan.FromSeconds(1),
            };
        });
        services
            .AddOptions<Microsoft.Extensions.Caching.Hybrid.HybridCacheOptions>()
            .Configure<IOptions<ValkeyOptions>>(
                (hybrid, valkey) => hybrid.MaximumPayloadBytes = valkey.Value.MaximumPayloadBytes
            );

        services.AddSingleton<IResilientCache, ResilientCache>();

        services.AddHealthChecks().AddCheck<ValkeyHealthCheck>(ValkeyHealthCheck.Name, tags: ["ready"]);

        return services;
    }
}
