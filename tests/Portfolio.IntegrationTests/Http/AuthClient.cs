using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>Tokens returned by a sign-in or refresh.</summary>
/// <param name="AccessToken">Compact JWT.</param>
/// <param name="RefreshToken">Opaque refresh token.</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
internal sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>Helpers that drive the real authentication endpoints the way a client would.</summary>
internal static class AuthClient
{
    public static async Task<HttpResponseMessage> SignInRawAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/tokens", UriKind.Relative),
            new { email, password },
            TestContext.Current.CancellationToken
        );

    public static async Task<IssuedTokens> SignInAsync(HttpClient client, TestAccount account)
    {
        using var response = await SignInRawAsync(client, account.Email, account.Password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ReadTokensAsync(response);
    }

    public static async Task<IssuedTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var root = json.RootElement;
        return new IssuedTokens(
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            root.GetProperty("expiresIn").GetInt32()
        );
    }

    public static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/tokens/refresh", UriKind.Relative),
            new { refreshToken },
            TestContext.Current.CancellationToken
        );

    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        Authorize(request, accessToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? accessToken,
        object? body = null
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        Authorize(request, accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        return json.RootElement.GetProperty("code").GetString()!;
    }

    private static void Authorize(HttpRequestMessage request, string? accessToken)
    {
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }
}
