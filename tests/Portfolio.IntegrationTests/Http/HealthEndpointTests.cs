namespace Portfolio.IntegrationTests.Http;

/// <summary>Liveness and readiness probes (ai/CONTAINERS.md §8.1, §8.2).</summary>
public sealed class HealthEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new("Production");

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Should_answer_probes_without_credentials(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri(path, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Should_keep_the_security_headers_on_probe_responses()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task Should_not_expose_detailed_health_data()
    {
        using var client = _factory.CreateClient();

        var body = await client.GetStringAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        body.ShouldNotContain("draining");
        body.ShouldNotContain("{");
    }
}
