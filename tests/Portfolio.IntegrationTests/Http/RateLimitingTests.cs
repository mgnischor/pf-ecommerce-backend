using System.Text.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>Sensitive flows are rate limited per client (OWASP API4/API6, ai/SECURITY.md §6.1).</summary>
public sealed class RateLimitingTests : IDisposable
{
    private readonly ApiFactory _factory = new("Production", authPermitLimit: 3);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Should_answer_429_with_retry_after_and_problem_details_once_the_limit_is_exceeded()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        foreach (var attempt in Enumerable.Range(0, 3))
        {
            using var allowed = await AuthClient.SignInRawAsync(
                client,
                "nobody@example.com",
                $"wrong-password-{attempt}-xxxx"
            );
            statuses.Add(allowed.StatusCode);
        }

        using var limited = await AuthClient.SignInRawAsync(client, "nobody@example.com", "wrong-password-last-xxxx");
        using var body = JsonDocument.Parse(
            await limited.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );

        statuses.ShouldAllBe(status => status == HttpStatusCode.Unauthorized);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        limited.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.RootElement.GetProperty("code").GetString().ShouldBe("RATE_LIMITED");
    }

    [Fact]
    public async Task Should_share_the_budget_between_sign_in_refresh_and_registration()
    {
        using var client = _factory.CreateClient();
        using var first = await AuthClient.SignInRawAsync(client, "nobody@example.com", "wrong-password-one-xxxx");
        using var second = await AuthClient.RefreshAsync(client, "not-a-token-at-all");
        using var third = await AuthClient.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email = "someone@example.com", password = "short" }
        );

        using var limited = await AuthClient.SignInRawAsync(client, "nobody@example.com", "wrong-password-two-xxxx");

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        _ = second;
        _ = third;
    }

    [Fact]
    public async Task Should_not_rate_limit_the_public_catalog_reads()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        foreach (var attempt in Enumerable.Range(0, 6))
        {
            using var response = await client.GetAsync(
                new Uri($"/api/v1/products?q=cafeteira{attempt}", UriKind.Relative),
                TestContext.Current.CancellationToken
            );
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldNotContain(HttpStatusCode.TooManyRequests);
    }
}
