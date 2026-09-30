namespace Portfolio.IntegrationTests.Http;

/// <summary>The OpenAPI document and Scalar are development-only (ai/SECURITY.md §6.6).</summary>
public sealed class DevelopmentApiReferenceTests : IDisposable
{
    private readonly ApiFactory _development = new("Development");

    public void Dispose() => _development.Dispose();

    [Fact]
    public async Task Should_serve_the_openapi_document_at_the_versioned_route()
    {
        using var client = _development.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/openapi/v1.json", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Should_serve_the_scalar_reference_without_the_api_content_security_policy()
    {
        using var client = _development.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/docs", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }
}
