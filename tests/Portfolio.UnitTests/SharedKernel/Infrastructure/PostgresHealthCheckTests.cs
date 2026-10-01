using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.SharedKernel.Infrastructure;

public sealed class PostgresHealthCheckTests
{
    [Fact]
    public async Task Should_report_unhealthy_without_leaking_connection_details_when_the_database_is_unreachable()
    {
        // Port 1 on loopback refuses immediately: no network, no database, no flakiness.
        await using var dataSource = new NpgsqlDataSourceBuilder(
            "Host=127.0.0.1;Port=1;Database=ecommerce;Username=app;Password=very-secret-value;Timeout=1"
        ).Build();
        var check = new PostgresHealthCheck(dataSource);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("PostgreSQL is not reachable.");
        result.Exception.ShouldBeNull();
        result.Data.Keys.ShouldBe(["error"]);
        string.Join(' ', result.Data.Values.Select(value => value.ToString())).ShouldNotContain("very-secret-value");
    }

    [Fact]
    public async Task Should_report_unhealthy_when_the_caller_cancels()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(
            "Host=127.0.0.1;Port=1;Database=ecommerce;Username=app;Timeout=1"
        ).Build();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await new PostgresHealthCheck(dataSource).CheckHealthAsync(
            new HealthCheckContext(),
            cancelled.Token
        );

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }
}
