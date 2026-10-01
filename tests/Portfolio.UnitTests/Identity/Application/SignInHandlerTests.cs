using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Identity.Support;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Application;

[Trait("Rule", "BR-IDN-003")]
public sealed class SignInHandlerTests
{
    private readonly IdentityWorld _world = new();

    private Task<Result<TokenPair>> SignInAsync(string? email, string? password) =>
        _world.SignIn.HandleAsync(new SignInCommand(email, password), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_issue_a_token_pair_and_store_only_the_refresh_token_hash()
    {
        var user = await _world.AddUserAsync();

        var result = await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        var pair = result.Value;
        pair.AccessToken.ShouldContain(user.Id.ToString());
        pair.RefreshToken.ShouldBe("rt-1");
        pair.ExpiresInSeconds.ShouldBe(600);
        var stored = (
            await _world.RefreshTokens.FindByHashAsync(
                _world.Codec.Hash(pair.RefreshToken),
                TestContext.Current.CancellationToken
            )
        ).ShouldNotBeNull();
        stored.TokenHash.ShouldNotBe(pair.RefreshToken);
        stored.UserId.ShouldBe(user.Id);
        _world.Audit.Events.ShouldBe(["sign_in_succeeded"]);
    }

    [Fact]
    public async Task Should_start_a_session_that_expires_at_the_family_lifetime_and_refresh_lifetime()
    {
        await _world.AddUserAsync();

        var pair = (await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword)).Value;

        var stored = (
            await _world.RefreshTokens.FindByHashAsync(
                _world.Codec.Hash(pair.RefreshToken),
                TestContext.Current.CancellationToken
            )
        ).ShouldNotBeNull();
        stored.ExpiresAt.ShouldBe(_world.Clock.GetUtcNow().AddDays(7));
        stored.FamilyExpiresAt.ShouldBe(_world.Clock.GetUtcNow().AddDays(30));
    }

    [Fact]
    public async Task Should_normalize_the_e_mail_before_looking_the_account_up()
    {
        await _world.AddUserAsync("ana.souza@example.com");

        var result = await SignInAsync("  ANA.SOUZA@Example.com ", IdentityWorld.StrongPassword);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "whatever-password-1")]
    [InlineData("", "whatever-password-1")]
    [InlineData("not-an-email", "whatever-password-1")]
    [InlineData("nobody@example.com", "whatever-password-1")]
    public async Task Should_fail_identically_and_burn_hashing_work_for_an_unknown_or_malformed_account(
        string? email,
        string password
    )
    {
        var result = await SignInAsync(email, password);

        result.ShouldFail().Code.ShouldBe("INVALID_CREDENTIALS");
        result.ShouldFail().Type.ShouldBe(ErrorType.Unauthorized);
        _world.Hasher.BurnCalls.ShouldBe(1);
        _world.Audit.Events.ShouldBe(["sign_in_failed"]);
    }

    [Fact]
    public async Task Should_fail_with_the_same_error_for_a_wrong_password_and_count_the_failure()
    {
        var user = await _world.AddUserAsync();

        var result = await SignInAsync("ana.souza@example.com", "wrong-password-123");

        result.ShouldFail().Code.ShouldBe("INVALID_CREDENTIALS");
        user.FailedSignIns.ShouldBe(1);
        (await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_lock_the_account_on_the_fifth_failure_and_audit_it()
    {
        var user = await _world.AddUserAsync();

        foreach (var attempt in Enumerable.Range(0, User.MaxFailedSignIns))
        {
            await SignInAsync("ana.souza@example.com", $"wrong-password-{attempt}");
        }

        user.IsLockedOut(_world.Clock.GetUtcNow()).ShouldBeTrue();
        _world.Audit.Events.ShouldContain("account_locked");
    }

    [Fact]
    public async Task Should_refuse_the_correct_password_while_locked_with_the_same_generic_error()
    {
        var user = await _world.AddUserAsync();
        foreach (var attempt in Enumerable.Range(0, User.MaxFailedSignIns))
        {
            user.RecordFailedSignIn(_world.Clock);
        }

        var result = await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        result.ShouldFail().Code.ShouldBe("INVALID_CREDENTIALS");
        _world.Hasher.BurnCalls.ShouldBe(1);
        _world.Audit.Events.ShouldBe(["sign_in_failed_locked"]);
    }

    [Fact]
    public async Task Should_accept_the_password_again_after_the_lockout_ends()
    {
        var user = await _world.AddUserAsync();
        foreach (var attempt in Enumerable.Range(0, User.MaxFailedSignIns))
        {
            user.RecordFailedSignIn(_world.Clock);
        }

        _world.Clock.Advance(User.LockoutDuration);
        var result = await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        result.IsSuccess.ShouldBeTrue();
        user.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task Should_refuse_a_deactivated_account_with_the_same_generic_error()
    {
        var user = await _world.AddUserAsync();
        user.Deactivate(_world.Clock);

        var result = await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        result.ShouldFail().Code.ShouldBe("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Should_not_hash_a_password_longer_than_the_maximum()
    {
        await _world.AddUserAsync();

        var result = await SignInAsync("ana.souza@example.com", new string('x', PasswordPolicy.MaxLength + 1));

        result.ShouldFail().Code.ShouldBe("INVALID_CREDENTIALS");
        _world.Hasher.HashCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_upgrade_an_outdated_password_verifier_on_a_successful_sign_in()
    {
        var user = User.Register(
            EmailAddress.Create("ana.souza@example.com").Value,
            FakePasswordHasher.OutdatedMarker + IdentityWorld.StrongPassword,
            AccessLevel.Public,
            _world.Clock
        );
        await _world.Users.AddAsync(user, TestContext.Current.CancellationToken);

        await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        user.PasswordHash.ShouldStartWith(FakePasswordHasher.Marker);
    }

    [Fact]
    public async Task Should_reset_the_failure_counter_on_success()
    {
        var user = await _world.AddUserAsync();
        await SignInAsync("ana.souza@example.com", "wrong-password-123");

        await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        user.FailedSignIns.ShouldBe(0);
    }

    [Fact]
    public async Task Should_put_the_accounts_current_level_and_token_version_in_the_access_token()
    {
        await _world.AddUserAsync(level: AccessLevel.Manager);

        var pair = (await SignInAsync("ana.souza@example.com", IdentityWorld.StrongPassword)).Value;

        pair.AccessToken.ShouldEndWith("Manager:1");
    }
}
