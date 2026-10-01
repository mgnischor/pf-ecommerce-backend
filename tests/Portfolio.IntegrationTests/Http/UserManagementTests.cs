using System.Net.Http.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Account administration and the privilege rules of BR-IDN-004: privileges only flow downward, nobody manages
/// themselves or an account that is not strictly below them (developers excepted), and every change takes effect
/// immediately instead of at token expiry.
/// </summary>
public sealed class UserManagementTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private string Admin => fixture.TokenFor("administrator").AccessToken;

    private string Developer => fixture.TokenFor("developer").AccessToken;

    private static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@example.com";

    private async Task<(Guid Id, string Email, string Password)> CreateAsync(string token, string level)
    {
        var email = NewEmail(level);
        var password = ApiFactory.RandomPassword();
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            token,
            new
            {
                email,
                password,
                accessLevel = level,
            }
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken
        );
        return (created.GetProperty("id").GetGuid(), email, password);
    }

    private async Task<IssuedTokens> SignInAsync(string email, string password)
    {
        using var response = await AuthClient.SignInRawAsync(fixture.Client, email, password);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await AuthClient.ReadTokensAsync(response);
    }

    private async Task<string> MeLevelAsync(string token)
    {
        using var response = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(
            TestContext.Current.CancellationToken
        );
        return me.GetProperty("accessLevel").GetString() ?? string.Empty;
    }

    [Theory]
    [InlineData("collaborator")]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task Should_let_an_administrator_create_accounts_up_to_their_own_level(string level)
    {
        var account = await CreateAsync(Admin, level);

        var tokens = await SignInAsync(account.Email, account.Password);

        (await MeLevelAsync(tokens.AccessToken)).ShouldBe(level);
    }

    [Fact]
    public async Task Should_let_a_developer_create_a_developer()
    {
        var account = await CreateAsync(Developer, "developer");

        var tokens = await SignInAsync(account.Email, account.Password);

        (await MeLevelAsync(tokens.AccessToken)).ShouldBe("developer");
    }

    [Fact]
    public async Task Should_refuse_an_administrator_granting_a_level_above_their_own()
    {
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            Admin,
            new
            {
                email = NewEmail("escalation"),
                password = ApiFactory.RandomPassword(),
                accessLevel = "developer",
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("ACCESS_LEVEL_ESCALATION");
    }

    [Theory]
    [InlineData("public")]
    [InlineData("superuser")]
    [InlineData("")]
    public async Task Should_refuse_an_account_with_an_invalid_or_non_staff_level(string level)
    {
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            Admin,
            new
            {
                email = NewEmail("invalid"),
                password = ApiFactory.RandomPassword(),
                accessLevel = level,
            }
        );

        ((int)response.StatusCode).ShouldBeOneOf(400, 422);
    }

    [Fact]
    public async Task Should_promote_an_account_and_invalidate_the_tokens_that_carry_the_old_level()
    {
        var account = await CreateAsync(Admin, "collaborator");
        var before = await SignInAsync(account.Email, account.Password);

        using var change = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/v1/users/{account.Id}/access-level",
            Admin,
            new { accessLevel = "manager" }
        );
        using var oldToken = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", before.AccessToken);
        var after = await SignInAsync(account.Email, account.Password);

        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        oldToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await MeLevelAsync(after.AccessToken)).ShouldBe("manager");
    }

    [Fact]
    public async Task Should_refuse_an_administrator_changing_another_administrator()
    {
        var peer = await CreateAsync(Admin, "administrator");

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/v1/users/{peer.Id}/access-level",
            Admin,
            new { accessLevel = "manager" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("USER_NOT_MANAGEABLE");
    }

    [Fact]
    public async Task Should_let_a_developer_manage_an_administrator()
    {
        var admin = await CreateAsync(Admin, "administrator");

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/v1/users/{admin.Id}/access-level",
            Developer,
            new { accessLevel = "manager" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Should_refuse_changing_the_callers_own_level()
    {
        var me = TokenForge.Read(Admin);

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/v1/users/{me.Subject}/access-level",
            Admin,
            new { accessLevel = "collaborator" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_report_an_unknown_account_as_not_found()
    {
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Put,
            $"/api/v1/users/{Guid.NewGuid()}/access-level",
            Admin,
            new { accessLevel = "manager" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_deactivate_an_account_and_revoke_every_token_it_holds()
    {
        var account = await CreateAsync(Admin, "collaborator");
        var tokens = await SignInAsync(account.Email, account.Password);

        using var deactivation = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/users/{account.Id}/deactivation",
            Admin
        );
        using var accessToken = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        using var refresh = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);
        using var signIn = await AuthClient.SignInRawAsync(fixture.Client, account.Email, account.Password);

        deactivation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        accessToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        signIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_treat_a_repeated_deactivation_as_success()
    {
        var account = await CreateAsync(Admin, "collaborator");
        using var first = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/users/{account.Id}/deactivation",
            Admin
        );

        using var second = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/users/{account.Id}/deactivation",
            Admin
        );

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Should_refuse_an_administrator_deactivating_a_developer()
    {
        var developer = await CreateAsync(Developer, "developer");

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/users/{developer.Id}/deactivation",
            Admin
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_reject_a_duplicate_e_mail_even_when_it_differs_only_by_case()
    {
        var account = await CreateAsync(Admin, "collaborator");

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            Admin,
            new
            {
                email = account.Email.ToUpperInvariant(),
                password = ApiFactory.RandomPassword(),
                accessLevel = "collaborator",
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("EMAIL_ALREADY_REGISTERED");
    }
}
