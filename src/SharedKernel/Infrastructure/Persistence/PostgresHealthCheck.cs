using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Readiness check that proves a connection can be opened and a statement run (ai/DATABASE.md §10.5). Reports
/// nothing about the failure beyond its type: connection details and server messages never reach a probe.
/// </summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    /// <summary>Name of the check.</summary>
    public const string Name = "postgres";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

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
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
            when (exception is NpgsqlException or OperationCanceledException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL is not reachable.",
                data: new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = exception.GetType().Name }
            );
        }
    }
}
