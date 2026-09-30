namespace Portfolio.IntegrationTests.Http;

/// <summary>HTTP hardening required by ai/SECURITY.md §6 and the error contract of ai/API_CONTRACTS.md §4.</summary>
public sealed class PipelineHardeningTests : IDisposable
{
    private readonly ApiFactory _production = new("Production");

    public void Dispose() => _production.Dispose();

    [Fact]
    public async Task Should_send_the_security_header_set_on_every_api_response()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/anything", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Cross-Origin-Resource-Policy").ShouldBe(["same-origin"]);
        response.Headers.Contains("Permissions-Policy").ShouldBeTrue();
    }

    [Fact]
    public async Task Should_not_reveal_the_server_technology_in_response_headers()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/anything", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.Headers.Contains("Server").ShouldBeFalse();
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public async Task Should_answer_an_unknown_route_with_rfc_9457_problem_details()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/anything", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("/api/v1/openapi/v1.json")]
    [InlineData("/api/v1/docs")]
    public async Task Should_not_expose_the_api_reference_outside_development(string path)
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri(path, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
