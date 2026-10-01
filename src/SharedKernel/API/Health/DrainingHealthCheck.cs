using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Portfolio.SharedKernel.API.Health;

/// <summary>
/// Readiness check that turns <c>Unhealthy</c> the moment shutdown begins, so the orchestrator stops sending
/// traffic while in-flight requests finish (ai/CONTAINERS.md §8.2: "readiness fails during drain").
/// </summary>
/// <param name="lifetime">Application lifetime that signals <c>SIGTERM</c>.</param>
internal sealed class DrainingHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    /// <summary>Name under which the check is registered.</summary>
    public const string Name = "draining";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            lifetime.ApplicationStopping.IsCancellationRequested
                ? HealthCheckResult.Unhealthy("The application is shutting down.")
                : HealthCheckResult.Healthy()
        );
}
