using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Portfolio.SharedKernel.API.Health;

namespace Portfolio.UnitTests.SharedKernel.API;

/// <summary>Readiness must fail the moment shutdown begins so traffic drains (ai/CONTAINERS.md §8.2).</summary>
public sealed class DrainingHealthCheckTests
{
    [Fact]
    public async Task Should_be_healthy_while_the_application_is_running()
    {
        using var lifetime = new FakeLifetime();

        var result = await new DrainingHealthCheck(lifetime).CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken
        );

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Should_turn_unhealthy_as_soon_as_shutdown_is_requested_so_the_orchestrator_stops_routing()
    {
        using var lifetime = new FakeLifetime();
        lifetime.StopApplication();

        var result = await new DrainingHealthCheck(lifetime).CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken
        );

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
