using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The mapped API: 25 versioned endpoints, default-deny authentication, Problem Details for operations
/// whose use case is not implemented yet (ai/API_CONTRACTS.md §2, §4 and ai/SECURITY.md §2.2).
/// </summary>
public sealed class ApiSurfaceTests : IDisposable
{
    private readonly ApiFactory _production = new("Production");

    public void Dispose() => _production.Dispose();

    [Fact]
    public void Should_map_exactly_the_documented_endpoints_under_the_versioned_base_path()
    {
        var mapped = _production
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(method =>
                    $"{method} {endpoint.RoutePattern.RawText}"
                )
            )
            .ToArray();

        var expected = ApiEndpoints.All.Select(endpoint => $"{endpoint.Method} {endpoint.Template}").ToArray();

        mapped.ShouldBe(expected, ignoreOrder: true);
        mapped.Length.ShouldBe(25);
    }

    [Theory]
    [MemberData(nameof(ApiEndpoints.Protected), MemberType = typeof(ApiEndpoints))]
    public async Task Should_reject_an_unauthenticated_request_to_a_protected_endpoint(string method, string url)
    {
        using var client = _production.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, MediaTypeFor(method));
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(url, UriKind.Relative))
        {
            Content = content,
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
    }

    [Fact]
    public async Task Should_answer_a_mapped_but_unimplemented_operation_with_a_problem_details_501()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            TestContext.Current.CancellationToken
        );
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.RootElement.GetProperty("status").GetInt32().ShouldBe(501);
        body.RootElement.GetProperty("code").GetString().ShouldBe("ENDPOINT_NOT_IMPLEMENTED");
        body.RootElement.GetProperty("instance").GetString().ShouldBe("/api/v1/products");
        body.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/api/v1/products/0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01")]
    [InlineData("/api/v1/products?limit=100&sort=-price&q=cafeteira")]
    public async Task Should_serve_the_anonymous_catalog_reads_without_credentials(string url)
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri(url, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Should_accept_a_well_formed_sign_in_request_without_credentials()
    {
        using var client = _production.CreateClient();
        using var content = Json("""{"email":"ana.souza@example.com","password":"example-password-not-real"}""");

        using var response = await client.PostAsync(
            new Uri("/api/v1/auth/tokens", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Should_accept_a_payment_webhook_without_bearer_credentials()
    {
        using var client = _production.CreateClient();
        using var content = Json("""{"id":"evt_test_0001","type":"payment.captured"}""");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/payments/webhooks/example-provider", UriKind.Relative)
        )
        {
            Content = content,
        };
        request.Headers.Add("X-Webhook-Signature", "sha256=0000");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Theory]
    [InlineData("/api/v1/products?limit=0")]
    [InlineData("/api/v1/products?limit=101")]
    [InlineData("/api/v1/products?sort=-price;drop")]
    [InlineData("/api/v1/products?q=a")]
    [InlineData("/api/v1/products?status=archived")]
    public async Task Should_reject_invalid_collection_parameters_with_a_problem_details_400(string url)
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri(url, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("""{"email":"not-an-email","password":"x"}""")]
    [InlineData("""{"email":"ana.souza@example.com"}""")]
    [InlineData("""{}""")]
    public async Task Should_reject_an_invalid_sign_in_body_without_echoing_the_password(string json)
    {
        using var client = _production.CreateClient();
        using var content = Json(json);

        using var response = await client.PostAsync(
            new Uri("/api/v1/auth/tokens", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldNotContain("\"x\"");
    }

    [Fact]
    public async Task Should_not_route_a_webhook_with_an_invalid_provider_key()
    {
        using var client = _production.CreateClient();
        using var content = Json("{}");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/payments/webhooks/Bad_Provider", UriKind.Relative)
        )
        {
            Content = content,
        };
        request.Headers.Add("X-Webhook-Signature", "sha256=0000");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_reject_a_webhook_without_a_signature()
    {
        using var client = _production.CreateClient();
        using var content = Json("{}");

        using var response = await client.PostAsync(
            new Uri("/api/v1/payments/webhooks/example-provider", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_not_route_a_malformed_identifier_to_an_operation()
    {
        using var client = _production.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/products/not-a-guid", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // PATCH only accepts JSON Merge Patch; a missing or wrong media type is rejected during routing, before authentication.
    private static string MediaTypeFor(string method) =>
        string.Equals(method, "PATCH", StringComparison.Ordinal) ? "application/merge-patch+json" : "application/json";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
