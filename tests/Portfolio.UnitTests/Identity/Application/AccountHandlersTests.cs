using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Identity.Support;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Application;

[Trait("Rule", "BR-IDN-002")]
public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new(new FakeBreachedPasswordScreen("breached-password-1"));

    private static EmailAddress Email => EmailAddress.Create("mariana.souza@example.com").Value;

    private Task<Result> ValidateAsync(string? password) =>
        _policy.ValidateAsync(password, Email, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("shortpass11")]
    public async Task Should_reject_a_password_shorter_than_twelve_characters(string? password)
    {
        var result = await ValidateAsync(password);

        result.ShouldFail().Code.ShouldBe("PASSWORD_LENGTH");
        result.ShouldFail().Parameters.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_accept_the_boundary_lengths_without_composition_rules()
    {
        (await ValidateAsync(new string('a', 11) + "b")).IsSuccess.ShouldBeTrue();
        (await ValidateAsync(string.Concat(Enumerable.Repeat("abcdefgh", 16)))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_reject_a_password_longer_than_one_hundred_twenty_eight_characters()
    {
        var result = await ValidateAsync(new string('a', 129));

        result.ShouldFail().Code.ShouldBe("PASSWORD_LENGTH");
    }

    [Fact]
    public async Task Should_reject_a_password_that_contains_the_account_name_in_any_case()
    {
        var result = await ValidateAsync("XX-Mariana.Souza-XX");

        result.ShouldFail().Code.ShouldBe("PASSWORD_CONTAINS_EMAIL");
    }

    [Fact]
    public async Task Should_reject_a_breached_password()
    {
        var result = await ValidateAsync("breached-password-1");

        result.ShouldFail().Code.ShouldBe("PASSWORD_COMPROMISED");
    }

    [Fact]
    public async Task Should_accept_a_long_passphrase()
    {
        (await ValidateAsync("correct horse battery staple")).IsSuccess.ShouldBeTrue();
    }
}

public sealed class RegisterCustomerHandlerTests
{
    private readonly IdentityWorld _world = new();

    private Task<Result<Guid>> RegisterAsync(string? email, string? password) =>
        _world.Register.HandleAsync(
            new RegisterCustomerCommand(email, password),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_create_a_public_account_with_a_hashed_password()
    {
        var id = (await RegisterAsync("ana.souza@example.com", IdentityWorld.StrongPassword)).Value;

        var user = (await _world.Users.GetByIdAsync(id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        user.AccessLevel.ShouldBe(AccessLevel.Public);
        user.PasswordHash.ShouldNotBe(IdentityWorld.StrongPassword);
        user.PasswordHash.ShouldStartWith(FakePasswordHasher.Marker);
        _world.Audit.Events.ShouldBe(["account_created:Public"]);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-006")]
    public async Task Should_pass_what_the_customer_typed_to_the_customers_context_without_storing_it()
    {
        var result = await _world.Register.HandleAsync(
            new RegisterCustomerCommand(
                "ana.souza@example.com",
                IdentityWorld.StrongPassword,
                "Ana Souza",
                "+5511987654321",
                "en",
                "Europe/Lisbon"
            ),
            TestContext.Current.CancellationToken
        );

        var user = (
            await _world.Users.GetByIdAsync(result.Value, TestContext.Current.CancellationToken)
        ).ShouldNotBeNull();
        var registered = user.DomainEvents.OfType<CustomerRegistered>().ShouldHaveSingleItem();
        registered.Email.ShouldBe("ana.souza@example.com");
        registered.FullName.ShouldBe("Ana Souza");
        registered.Phone.ShouldBe("+5511987654321");
        registered.Locale.ShouldBe("en");
        registered.TimeZone.ShouldBe("Europe/Lisbon");
    }

    [Fact]
    public async Task Should_register_a_customer_who_typed_nothing_about_themselves()
    {
        var id = (await RegisterAsync("ana.souza@example.com", IdentityWorld.StrongPassword)).Value;

        var user = (await _world.Users.GetByIdAsync(id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var registered = user.DomainEvents.OfType<CustomerRegistered>().ShouldHaveSingleItem();
        registered.FullName.ShouldBeNull();
        registered.Phone.ShouldBeNull();
    }

    [Fact]
    [Trait("Rule", "BR-IDN-001")]
    public async Task Should_reject_a_duplicate_e_mail_regardless_of_case()
    {
        await RegisterAsync("ana.souza@example.com", IdentityWorld.StrongPassword);

        var result = await RegisterAsync("ANA.SOUZA@example.com", IdentityWorld.StrongPassword);

        result.ShouldFail().Code.ShouldBe("EMAIL_ALREADY_REGISTERED");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData("not-an-email", "EMAIL_INVALID")]
    [InlineData("ana.souza@example.com", "PASSWORD_LENGTH")]
    public async Task Should_reject_invalid_input_and_create_nothing(string email, string expectedCode)
    {
        var result = await RegisterAsync(
            email,
            email.StartsWith("not", StringComparison.Ordinal) ? IdentityWorld.StrongPassword : "short"
        );

        result.ShouldFail().Code.ShouldBe(expectedCode);
        _world.Audit.Events.ShouldBeEmpty();
    }
}

[Trait("Rule", "BR-IDN-004")]
public sealed class CreateUserHandlerTests
{
    private readonly IdentityWorld _world = new();

    private Task<Result<Guid>> CreateAsync(AccessLevel actor, string? level, string email = "new.staff@example.com") =>
        _world.CreateUser.HandleAsync(
            new CreateUserCommand(Guid.CreateVersion7(), actor, email, IdentityWorld.StrongPassword, level),
            TestContext.Current.CancellationToken
        );

    [Theory]
    [InlineData("collaborator")]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task Should_let_an_administrator_create_staff_up_to_their_level(string level)
    {
        var id = (await CreateAsync(AccessLevel.Administrator, level)).Value;

        var user = (await _world.Users.GetByIdAsync(id, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        user.AccessLevel.ToWireName().ShouldBe(level);
    }

    [Fact]
    public async Task Should_let_a_developer_create_a_developer()
    {
        (await CreateAsync(AccessLevel.Developer, "developer")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_refuse_an_administrator_creating_a_developer_and_audit_the_attempt()
    {
        var result = await CreateAsync(AccessLevel.Administrator, "developer");

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_ESCALATION");
        _world.Audit.Events.ShouldBe(["privilege_denied:ACCESS_LEVEL_ESCALATION"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Should_refuse_callers_below_administrator(int actor)
    {
        var result = await CreateAsync((AccessLevel)actor, "collaborator");

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INSUFFICIENT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("public")]
    [InlineData("superuser")]
    [InlineData("Manager")]
    public async Task Should_refuse_an_invalid_or_public_level(string? level)
    {
        var result = await CreateAsync(AccessLevel.Developer, level);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INVALID");
    }

    [Fact]
    public async Task Should_reject_a_duplicate_e_mail()
    {
        await CreateAsync(AccessLevel.Administrator, "manager");

        var result = await CreateAsync(AccessLevel.Administrator, "collaborator");

        result.ShouldFail().Code.ShouldBe("EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Should_apply_the_password_policy_to_the_new_account()
    {
        var result = await _world.CreateUser.HandleAsync(
            new CreateUserCommand(
                Guid.CreateVersion7(),
                AccessLevel.Administrator,
                "new.staff@example.com",
                "short",
                "manager"
            ),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PASSWORD_LENGTH");
    }
}

[Trait("Rule", "BR-IDN-004")]
public sealed class ChangeUserAccessLevelHandlerTests
{
    private readonly IdentityWorld _world = new();

    private Task<Result> ChangeAsync(Guid actorId, AccessLevel actor, Guid target, string? level) =>
        _world.ChangeLevel.HandleAsync(
            new ChangeUserAccessLevelCommand(actorId, actor, target, level),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_promote_an_account_and_audit_the_privilege_change()
    {
        var target = await _world.AddUserAsync(level: AccessLevel.Collaborator);

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id, "manager");

        result.IsSuccess.ShouldBeTrue();
        target.AccessLevel.ShouldBe(AccessLevel.Manager);
        target.TokenVersion.ShouldBe(2);
        _world.Audit.Events.ShouldBe(["access_level_changed:Collaborator->Manager"]);
    }

    [Fact]
    public async Task Should_not_audit_a_change_to_the_same_level()
    {
        var target = await _world.AddUserAsync(level: AccessLevel.Manager);

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id, "manager");

        result.IsSuccess.ShouldBeTrue();
        _world.Audit.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_refuse_granting_a_level_above_the_callers()
    {
        var target = await _world.AddUserAsync(level: AccessLevel.Collaborator);

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id, "developer");

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_ESCALATION");
        target.AccessLevel.ShouldBe(AccessLevel.Collaborator);
    }

    [Fact]
    public async Task Should_refuse_managing_a_peer_administrator()
    {
        var peer = await _world.AddUserAsync("peer.admin@example.com", AccessLevel.Administrator);

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Administrator, peer.Id, "manager");

        result.ShouldFail().Code.ShouldBe("USER_NOT_MANAGEABLE");
        peer.AccessLevel.ShouldBe(AccessLevel.Administrator);
    }

    [Fact]
    public async Task Should_refuse_changing_ones_own_level()
    {
        var self = await _world.AddUserAsync("self.admin@example.com", AccessLevel.Administrator);

        var result = await ChangeAsync(self.Id, AccessLevel.Administrator, self.Id, "collaborator");

        result.ShouldFail().Code.ShouldBe("USER_NOT_MANAGEABLE");
    }

    [Fact]
    public async Task Should_report_an_unknown_account_as_not_found()
    {
        var result = await ChangeAsync(
            Guid.CreateVersion7(),
            AccessLevel.Administrator,
            Guid.CreateVersion7(),
            "manager"
        );

        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("root")]
    public async Task Should_refuse_an_unknown_level(string? level)
    {
        var target = await _world.AddUserAsync();

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Developer, target.Id, level);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INVALID");
    }

    [Fact]
    public async Task Should_let_a_developer_demote_an_administrator()
    {
        var admin = await _world.AddUserAsync("the.admin@example.com", AccessLevel.Administrator);

        var result = await ChangeAsync(Guid.CreateVersion7(), AccessLevel.Developer, admin.Id, "manager");

        result.IsSuccess.ShouldBeTrue();
    }
}

[Trait("Rule", "BR-IDN-006")]
public sealed class DeactivateUserHandlerTests
{
    private readonly IdentityWorld _world = new();

    private Task<Result> DeactivateAsync(Guid actorId, AccessLevel actor, Guid target) =>
        _world.Deactivate.HandleAsync(
            new DeactivateUserCommand(actorId, actor, target),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_deactivate_and_revoke_every_session_of_the_account()
    {
        var target = await _world.AddUserAsync(level: AccessLevel.Collaborator);
        var pair = (
            await _world.SignIn.HandleAsync(
                new SignInCommand("ana.souza@example.com", IdentityWorld.StrongPassword),
                TestContext.Current.CancellationToken
            )
        ).Value;
        _world.Audit.Events.Clear();

        var result = await DeactivateAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id);

        result.IsSuccess.ShouldBeTrue();
        target.Status.ShouldBe(UserStatus.Deactivated);
        (await _world.RefreshTokens.ListByUserAsync(target.Id, TestContext.Current.CancellationToken)).ShouldAllBe(t =>
            t.RevokedAt != null
        );
        (
            await _world.Refresh.HandleAsync(
                new RefreshTokenCommand(pair.RefreshToken),
                TestContext.Current.CancellationToken
            )
        ).IsFailure.ShouldBeTrue();
        _world.Audit.Events.ShouldContain("account_deactivated");
    }

    [Fact]
    public async Task Should_be_idempotent_and_audit_only_the_first_deactivation()
    {
        var target = await _world.AddUserAsync();

        await DeactivateAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id);
        var second = await DeactivateAsync(Guid.CreateVersion7(), AccessLevel.Administrator, target.Id);

        second.IsSuccess.ShouldBeTrue();
        _world.Audit.Events.Count(e => e == "account_deactivated").ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Should_refuse_callers_below_administrator(int actor)
    {
        var target = await _world.AddUserAsync();

        var result = await DeactivateAsync(Guid.CreateVersion7(), (AccessLevel)actor, target.Id);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INSUFFICIENT");
        target.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task Should_refuse_deactivating_a_developer_as_an_administrator()
    {
        var developer = await _world.AddUserAsync("the.dev@example.com", AccessLevel.Developer);

        var result = await DeactivateAsync(Guid.CreateVersion7(), AccessLevel.Administrator, developer.Id);

        result.ShouldFail().Code.ShouldBe("USER_NOT_MANAGEABLE");
    }

    [Fact]
    public async Task Should_report_an_unknown_account_as_not_found()
    {
        var result = await DeactivateAsync(Guid.CreateVersion7(), AccessLevel.Administrator, Guid.CreateVersion7());

        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }
}

public sealed class RevokeTokenHandlerTests
{
    private readonly IdentityWorld _world = new();

    [Fact]
    public async Task Should_revoke_the_session_and_block_the_access_token_until_it_would_expire()
    {
        var user = await _world.AddUserAsync();
        var pair = (
            await _world.SignIn.HandleAsync(
                new SignInCommand("ana.souza@example.com", IdentityWorld.StrongPassword),
                TestContext.Current.CancellationToken
            )
        ).Value;
        var expiresAt = _world.Clock.GetUtcNow().AddMinutes(10);

        var result = await _world.Revoke.HandleAsync(
            new RevokeTokenCommand(pair.RefreshToken, "jti-1", expiresAt, user.Id),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        (await _world.Revoked.IsRevokedAsync("jti-1", TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken)).ShouldAllBe(t =>
            t.RevokedAt != null
        );
    }

    [Fact]
    public async Task Should_succeed_for_an_unknown_refresh_token_so_it_cannot_probe_tokens()
    {
        var result = await _world.Revoke.HandleAsync(
            new RevokeTokenCommand("never-issued", null, null, null),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_stop_blocking_the_access_token_once_it_has_expired()
    {
        var expiresAt = _world.Clock.GetUtcNow().AddMinutes(10);
        await _world.Revoke.HandleAsync(
            new RevokeTokenCommand(null, "jti-2", expiresAt, null),
            TestContext.Current.CancellationToken
        );

        _world.Clock.Advance(TimeSpan.FromMinutes(11));

        (await _world.Revoked.IsRevokedAsync("jti-2", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
}
