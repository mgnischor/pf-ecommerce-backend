using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Readiness check of Valkey (ai/CONTAINERS.md §8.1). A cache outage is <b>degraded</b>, not unhealthy: the application
/// keeps serving from PostgreSQL, so the instance stays in the load balancer. Like the PostgreSQL check, it reports
/// only that Valkey is unreachable and the exception type, never an address or a server message.
/// </summary>
internal sealed class ValkeyHealthCheck(ValkeyConnection connection) : IHealthCheck
{
    /// <summary>Name of the check.</summary>
    public const string Name = "valkey";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var multiplexer = await connection.GetAsync().WaitAsync(Budget, cancellationToken);
            await multiplexer.GetDatabase().PingAsync().WaitAsync(Budget, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded(
                "Valkey is not reachable; the application is serving without its cache.",
                data: new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = exception.GetType().Name }
            );
        }
    }
}
