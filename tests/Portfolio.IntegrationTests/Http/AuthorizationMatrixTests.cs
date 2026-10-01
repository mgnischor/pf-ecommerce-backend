using System.Text;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Every protected endpoint against every principal: anonymous, then each of the five access levels. The
/// outcome must follow the policy exactly (ai/TESTS.md §4.3: unauthenticated → 401, insufficient level → 403,
/// sufficient level → past authorization), so a mis-declared attribute on any endpoint fails a test.
/// </summary>
public sealed class AuthorizationMatrixTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    [Theory]
    [MemberData(nameof(ApiEndpoints.Matrix), MemberType = typeof(ApiEndpoints))]
    public async Task Should_apply_the_access_level_policy_to_every_protected_endpoint(
        string method,
        string url,
        string minLevel,
        string principal
    )
    {
        using var content = new StringContent("{}", Encoding.UTF8, MediaTypeFor(method));
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(url, UriKind.Relative))
        {
            Content = content,
        };
        if (!string.Equals(principal, ApiEndpoints.Anonymous, StringComparison.Ordinal))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                fixture.TokenFor(principal).AccessToken
            );
        }

        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Expected(minLevel, principal).ShouldBe(Classify(response.StatusCode), $"{method} {url} as {principal}");
    }

    // PATCH only accepts JSON Merge Patch; a missing or wrong media type is rejected during routing.
    private static string MediaTypeFor(string method) =>
        string.Equals(method, "PATCH", StringComparison.Ordinal) ? "application/merge-patch+json" : "application/json";

    private static string Expected(string minLevel, string principal)
    {
        if (string.Equals(principal, ApiEndpoints.Anonymous, StringComparison.Ordinal))
        {
            return "unauthenticated";
        }

        return TestAccounts.Rank(principal) < TestAccounts.Rank(minLevel) ? "forbidden" : "allowed";
    }

    private static string Classify(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.Unauthorized => "unauthenticated",
            HttpStatusCode.Forbidden => "forbidden",
            // 501 is the expected outcome of a stub that passed authorization; any other 5xx is a crash.
            >= HttpStatusCode.InternalServerError and not HttpStatusCode.NotImplemented => "server error",
            _ => "allowed",
        };
}
