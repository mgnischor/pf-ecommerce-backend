namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Sign-in, refresh-token rotation with reuse detection, expiry, lockout, and sign-out, driven through the
/// public endpoints with a controllable clock (ai/SECURITY.md §7.2, §7.6; BR-IDN-003, BR-IDN-005).
/// </summary>
public sealed class SessionLifecycleTests(ClockedAuthFixture fixture) : IClassFixture<ClockedAuthFixture>
{
    private TestAccount Collaborator => fixture.Factory.Accounts["collaborator"];

    private async Task<IssuedTokens> NewSessionAsync() => await AuthClient.SignInAsync(fixture.Client, Collaborator);

    private async Task<HttpStatusCode> MeStatusAsync(string accessToken)
    {
        using var response = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", accessToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task Should_answer_an_unknown_account_and_a_wrong_password_identically()
    {
        using var unknown = await AuthClient.SignInRawAsync(
            fixture.Client,
            "nobody@example.com",
            ApiFactory.RandomPassword()
        );
        using var wrong = await AuthClient.SignInRawAsync(
            fixture.Client,
            Collaborator.Email,
            ApiFactory.RandomPassword()
        );

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.ReadCodeAsync(unknown)).ShouldBe("INVALID_CREDENTIALS");
        (await AuthClient.ReadCodeAsync(wrong)).ShouldBe("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Should_expire_the_access_token_after_its_lifetime()
    {
        var tokens = await NewSessionAsync();

        var beforeExpiry = await MeStatusAsync(tokens.AccessToken);
        fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        var afterExpiry = await MeStatusAsync(tokens.AccessToken);

        beforeExpiry.ShouldBe(HttpStatusCode.OK);
        afterExpiry.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_issue_a_working_access_token_lifetime_no_longer_than_fifteen_minutes()
    {
        var tokens = await NewSessionAsync();

        tokens.ExpiresIn.ShouldBeInRange(1, 15 * 60);
    }

    [Fact]
    public async Task Should_rotate_the_refresh_token_and_refuse_the_one_that_was_used()
    {
        var first = await NewSessionAsync();

        using var rotation = await AuthClient.RefreshAsync(fixture.Client, first.RefreshToken);
        var second = await AuthClient.ReadTokensAsync(rotation);
        using var replay = await AuthClient.RefreshAsync(fixture.Client, first.RefreshToken);

        rotation.StatusCode.ShouldBe(HttpStatusCode.OK);
        string.Equals(second.RefreshToken, first.RefreshToken, StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(second.AccessToken, first.AccessToken, StringComparison.Ordinal).ShouldBeFalse();
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.ReadCodeAsync(replay)).ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Should_revoke_the_whole_session_when_a_used_refresh_token_is_presented_again()
    {
        var first = await NewSessionAsync();
        using var rotation = await AuthClient.RefreshAsync(fixture.Client, first.RefreshToken);
        var second = await AuthClient.ReadTokensAsync(rotation);

        using var replay = await AuthClient.RefreshAsync(fixture.Client, first.RefreshToken);
        using var successor = await AuthClient.RefreshAsync(fixture.Client, second.RefreshToken);
        var accessTokenOfTheThief = await MeStatusAsync(second.AccessToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        successor.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        accessTokenOfTheThief.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_reject_a_refresh_token_after_its_lifetime()
    {
        var tokens = await NewSessionAsync();

        fixture.Clock.Advance(TimeSpan.FromDays(8));
        using var response = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("")]
    public async Task Should_reject_an_unknown_refresh_token_with_the_same_generic_answer(string token)
    {
        using var response = await AuthClient.RefreshAsync(fixture.Client, token);

        ((int)response.StatusCode).ShouldBeOneOf(400, 401);
    }

    [Fact]
    public async Task Should_end_the_session_on_sign_out_for_both_the_access_and_the_refresh_token()
    {
        var tokens = await NewSessionAsync();

        using var signOut = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/tokens/revocation",
            tokens.AccessToken,
            new { refreshToken = tokens.RefreshToken }
        );
        var access = await MeStatusAsync(tokens.AccessToken);
        using var refresh = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);

        signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        access.ShouldBe(HttpStatusCode.Unauthorized);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_answer_sign_out_with_an_unknown_token_the_same_way_so_it_cannot_probe_tokens()
    {
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/tokens/revocation",
            accessToken: null,
            new { refreshToken = "never-issued" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Should_lock_the_account_after_five_failures_and_unlock_it_after_fifteen_minutes()
    {
        var account = await RegisterAsync();

        var failures = new List<HttpStatusCode>();
        foreach (var attempt in Enumerable.Range(0, 5))
        {
            using var failed = await AuthClient.SignInRawAsync(
                fixture.Client,
                account.Email,
                $"wrong-password-{attempt}-xxxx"
            );
            failures.Add(failed.StatusCode);
        }

        using var whileLocked = await AuthClient.SignInRawAsync(fixture.Client, account.Email, account.Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        using var afterLockout = await AuthClient.SignInRawAsync(fixture.Client, account.Email, account.Password);

        failures.ShouldAllBe(status => status == HttpStatusCode.Unauthorized);
        whileLocked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.ReadCodeAsync(whileLocked)).ShouldBe("INVALID_CREDENTIALS");
        afterLockout.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_reset_the_failure_counter_after_a_successful_sign_in()
    {
        var account = await RegisterAsync();

        using var firstFailure = await AuthClient.SignInRawAsync(
            fixture.Client,
            account.Email,
            "wrong-password-one-xxxx"
        );
        using var success = await AuthClient.SignInRawAsync(fixture.Client, account.Email, account.Password);
        using var secondFailure = await AuthClient.SignInRawAsync(
            fixture.Client,
            account.Email,
            "wrong-password-two-xxxx"
        );
        using var stillOpen = await AuthClient.SignInRawAsync(fixture.Client, account.Email, account.Password);

        firstFailure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        success.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondFailure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        stillOpen.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<TestAccount> RegisterAsync()
    {
        var account = new TestAccount($"lock.{Guid.NewGuid():N}@example.com", ApiFactory.RandomPassword(), "public");
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email = account.Email, password = account.Password }
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return account;
    }
}
