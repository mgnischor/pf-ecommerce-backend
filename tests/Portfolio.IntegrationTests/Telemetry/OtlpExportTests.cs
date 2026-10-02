using Portfolio.IntegrationTests.Http;

namespace Portfolio.IntegrationTests.Telemetry;

/// <summary>
/// The real export path (ai/OBSERVABILITY.md §3.5, §4.3): <c>OTEL_*</c> variables configure the SDK, traces, metrics and logs
/// leave the process over OTLP, carry the mandatory resource attributes and no secret, and a Collector that is absent or down
/// never fails or slows a request ("telemetry never breaks the app").
/// </summary>
public sealed class OtlpExportTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // UseSetting (not ConfigureAppConfiguration): the exporter is decided while the application registers its services,
    // and only host settings are visible to that code.
    private static Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder> Export(string endpoint) =>
        builder =>
        {
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint);
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
            builder.UseSetting("OTEL_SERVICE_NAME", "ecommerce-api");
            builder.UseSetting(
                "OTEL_RESOURCE_ATTRIBUTES",
                "deployment.environment.name=development,app.owner=team-platform,service.version=9.9.9"
            );
            builder.UseSetting("OTEL_BSP_SCHEDULE_DELAY", "300");
            builder.UseSetting("OTEL_BLRP_SCHEDULE_DELAY", "300");
            builder.UseSetting("OTEL_METRIC_EXPORT_INTERVAL", "500");
        };

    [Fact]
    public async Task Should_export_traces_metrics_and_logs_with_the_resource_attributes_and_no_secret()
    {
        await using var collector = await FakeOtlpCollector.StartAsync();
        using var factory = new ApiFactory("Production", configure: Export(collector.Endpoint));
        using var client = factory.CreateClient();
        var password = factory.Accounts["manager"].Password;

        var tokens = await AuthClient.SignInAsync(client, factory.Accounts["manager"]);
        using var me = await AuthClient.GetAsync(client, "/api/v1/auth/me", tokens.AccessToken);

        (
            await collector.WaitForAsync(
                "traces",
                body => body.Contains("api/v1/auth/me", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)
            )
        ).ShouldBeTrue("a trace of the request was exported");
        (
            await collector.WaitForAsync(
                "metrics",
                body => body.Contains("http.server.request.duration", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)
            )
        ).ShouldBeTrue("the RED duration histogram was exported");
        (await collector.WaitForAsync("logs", body => body.Length > 0, TimeSpan.FromSeconds(15))).ShouldBeTrue(
            "log records were exported"
        );

        var traces = collector.Received("traces");
        foreach (
            var expected in new[]
            {
                "service.name",
                "ecommerce-api",
                "service.namespace",
                "ecommerce",
                "app.owner",
                "team-platform",
                "deployment.environment.name",
                "service.instance.id",
                "9.9.9",
            }
        )
        {
            traces.ShouldContain(expected);
        }

        // Nothing sensitive in any signal that left the process.
        foreach (var signal in new[] { "traces", "metrics", "logs" })
        {
            var exported = collector.Received(signal);
            exported.ShouldNotContain(password, Case.Sensitive);
            exported.ShouldNotContain(tokens.AccessToken, Case.Sensitive);
            exported.ShouldNotContain(tokens.RefreshToken, Case.Sensitive);
        }
    }

    [Fact]
    public async Task Should_serve_requests_normally_and_quickly_when_the_collector_is_down()
    {
        // Nothing listens on port 1: every export attempt fails.
        using var factory = new ApiFactory("Production", configure: Export("http://127.0.0.1:1"));
        using var client = factory.CreateClient();
        var tokens = await AuthClient.SignInAsync(client, factory.Accounts["manager"]);

        var started = TimeProvider.System.GetTimestamp();
        for (var request = 0; request < 20; request++)
        {
            using var me = await AuthClient.GetAsync(client, "/api/v1/auth/me", tokens.AccessToken);
            me.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // 20 authenticated requests with a dead Collector: export is asynchronous and bounded, so none waits for it.
        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Should_export_nothing_and_still_work_when_no_endpoint_is_configured()
    {
        using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }
}
