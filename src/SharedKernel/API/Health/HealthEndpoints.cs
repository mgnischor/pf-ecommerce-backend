using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Portfolio.SharedKernel.API.Health;

/// <summary>
/// Liveness and readiness endpoints (ai/CONTAINERS.md §8.1). Liveness runs no checks (it only proves the
/// process answers); readiness runs the checks tagged <see cref="ReadyTag"/>. Dependency checks for PostgreSQL,
/// Valkey (degraded, not unhealthy: it is a cache), and RabbitMQ join readiness when those integrations exist.
/// The endpoints are anonymous because probes carry no credentials: they must be reachable only from the
/// orchestrator or the internal network, never routed through the public ingress (ai/SECURITY.md §6.2).
/// </summary>
internal static class HealthEndpoints
{
    /// <summary>Tag of the checks that gate readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Liveness route.</summary>
    public const string LivePath = "/health/live";

    /// <summary>Readiness route.</summary>
    public const string ReadyPath = HealthProbe.ReadyPath;

    /// <summary>Registers the readiness checks that exist today.</summary>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DrainingHealthCheck>(DrainingHealthCheck.Name, tags: [ReadyTag]);
        return services;
    }

    /// <summary>Maps the two probe endpoints.</summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints
            .MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return endpoints;
    }
}
