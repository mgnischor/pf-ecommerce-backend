using System.Net.Http.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>Self-registration: always a public-level account, policy-checked passwords, no enumeration via mass assignment.</summary>
public sealed class RegistrationTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static string NewEmail() => $"customer.{Guid.NewGuid():N}@example.com";

    private async Task<HttpResponseMessage> RegisterAsync(object body) =>
        await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            body
        );

    [Fact]
    public async Task Should_create_a_public_level_account_that_can_sign_in()
    {
        var email = NewEmail();
        var password = ApiFactory.RandomPassword();

        using var created = await RegisterAsync(new { email, password });
        using var signIn = await AuthClient.SignInRawAsync(fixture.Client, email, password);
        var tokens = await AuthClient.ReadTokensAsync(signIn);
        using var me = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        var profile = await me.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken
        );

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        profile.GetProperty("accessLevel").GetString().ShouldBe("public");
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("administrator")]
    public async Task Should_ignore_an_access_level_smuggled_into_the_registration_body(string level)
    {
        var email = NewEmail();
        var password = ApiFactory.RandomPassword();

        using var created = await RegisterAsync(
            new
            {
                email,
                password,
                accessLevel = level,
                role = level,
                isAdmin = true,
            }
        );
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var diagnostics = await AuthClient.GetAsync(
            fixture.Client,
            "/api/v1/diagnostics/runtime",
            tokens.AccessToken
        );
        using var users = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            tokens.AccessToken,
            new { }
        );

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        diagnostics.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        users.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_reject_a_duplicate_e_mail_regardless_of_case()
    {
        var email = NewEmail();
        using var first = await RegisterAsync(new { email, password = ApiFactory.RandomPassword() });

        using var duplicate = await RegisterAsync(
            new { email = email.ToUpperInvariant(), password = ApiFactory.RandomPassword() }
        );

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(duplicate)).ShouldBe("EMAIL_ALREADY_REGISTERED");
    }

    [Theory]
    [InlineData("short", "PASSWORD_LENGTH")]
    [InlineData("password12345", "PASSWORD_COMPROMISED")]
    [InlineData("aaaaaaaaaaaaaaaa", "PASSWORD_COMPROMISED")]
    public async Task Should_enforce_the_password_policy(string password, string expectedCode)
    {
        using var response = await RegisterAsync(new { email = NewEmail(), password });
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body.ShouldContain(expectedCode);
        body.ShouldNotContain(password, Case.Sensitive);
    }

    [Fact]
    public async Task Should_reject_a_password_that_contains_the_account_name()
    {
        using var response = await RegisterAsync(
            new { email = "mariana.souza@example.com", password = "xx-mariana.souza-xx" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PASSWORD_CONTAINS_EMAIL");
    }

    [Fact]
    public async Task Should_accept_a_long_passphrase_without_composition_rules()
    {
        using var response = await RegisterAsync(
            new { email = NewEmail(), password = "correct horse battery staple again" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_reject_a_password_longer_than_the_maximum()
    {
        using var response = await RegisterAsync(new { email = NewEmail(), password = new string('x', 129) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
