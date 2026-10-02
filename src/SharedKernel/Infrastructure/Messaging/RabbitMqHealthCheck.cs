using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Readiness check of RabbitMQ (ai/CONTAINERS.md §8.1). A broker outage is <b>degraded</b>, not unhealthy: requests are
/// still served and their events accumulate in the outbox until the broker is back. Reports only the exception type.
/// </summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    /// <summary>Name of the check.</summary>
    public const string Name = "rabbitmq";

    private const string Unreachable = "RabbitMQ is not reachable; events are accumulating in the outbox.";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Budget);

        try
        {
            var open = await connection.GetAsync(timeout.Token);
            return open.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(Unreachable);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded(
                Unreachable,
                data: new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = exception.GetType().Name }
            );
        }
    }
}
