using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Domain;

public sealed class UserRegistrationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private static EmailAddress Email => EmailAddress.Create("ana.souza@example.com").Value;

    [Fact]
    public void Should_register_an_active_account_with_the_initial_versions()
    {
        var user = User.Register(Email, "hash", AccessLevel.Manager, _clock);

        user.Status.ShouldBe(UserStatus.Active);
        user.AccessLevel.ShouldBe(AccessLevel.Manager);
        user.TokenVersion.ShouldBe(1);
        user.Version.ShouldBe(AggregateRoot.InitialVersion);
        user.FailedSignIns.ShouldBe(0);
        user.LockedUntil.ShouldBeNull();
        user.CanSignIn(_clock.GetUtcNow()).ShouldBeTrue();
    }

    [Fact]
    public void Should_raise_a_registered_event_that_carries_no_personal_data()
    {
        var user = User.Register(Email, "hash", AccessLevel.Collaborator, _clock);

        var domainEvent = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserRegistered>();
        domainEvent.AggregateId.ShouldBe(user.Id);
        domainEvent.AggregateVersion.ShouldBe(user.Version);
        domainEvent.Level.ShouldBe(AccessLevel.Collaborator);
        domainEvent.ToString().ShouldNotContain("ana.souza");
        domainEvent.ToString().ShouldNotContain("hash");
    }

    [Fact]
    public void Should_reject_an_undefined_access_level()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => User.Register(Email, "hash", (AccessLevel)42, _clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_reject_a_missing_password_hash(string hash)
    {
        Should.Throw<ArgumentException>(() => User.Register(Email, hash, AccessLevel.Public, _clock));
    }
}

[Trait("Rule", "BR-IDN-003")]
public sealed class UserLockoutTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private User NewUser() =>
        User.Register(EmailAddress.Create("ana.souza@example.com").Value, "hash", AccessLevel.Public, _clock);

    [Fact]
    public void Should_not_lock_before_the_fifth_consecutive_failure()
    {
        var user = NewUser();

        foreach (var _ in Enumerable.Range(0, User.MaxFailedSignIns - 1))
        {
            user.RecordFailedSignIn(_clock);
        }

        user.IsLockedOut(_clock.GetUtcNow()).ShouldBeFalse();
        user.FailedSignIns.ShouldBe(User.MaxFailedSignIns - 1);
    }

    [Fact]
    public void Should_lock_for_fifteen_minutes_on_the_fifth_failure()
    {
        var user = NewUser();

        foreach (var _ in Enumerable.Range(0, User.MaxFailedSignIns))
        {
            user.RecordFailedSignIn(_clock);
        }

        user.IsLockedOut(_clock.GetUtcNow()).ShouldBeTrue();
        user.CanSignIn(_clock.GetUtcNow()).ShouldBeFalse();
        user.LockedUntil.ShouldBe(TestClock.Start.Add(User.LockoutDuration));
        user.FailedSignIns.ShouldBe(0);
    }

    [Fact]
    public void Should_unlock_exactly_when_the_lockout_ends()
    {
        var user = NewUser();
        foreach (var _ in Enumerable.Range(0, User.MaxFailedSignIns))
        {
            user.RecordFailedSignIn(_clock);
        }

        _clock.Advance(User.LockoutDuration - TimeSpan.FromSeconds(1));
        var justBefore = user.IsLockedOut(_clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromSeconds(1));
        var atTheBoundary = user.IsLockedOut(_clock.GetUtcNow());

        justBefore.ShouldBeTrue();
        atTheBoundary.ShouldBeFalse();
    }

    [Fact]
    public void Should_clear_failures_and_lockout_after_a_successful_sign_in()
    {
        var user = NewUser();
        user.RecordFailedSignIn(_clock);
        user.RecordFailedSignIn(_clock);

        user.RecordSuccessfulSignIn(_clock);

        user.FailedSignIns.ShouldBe(0);
        user.LockedUntil.ShouldBeNull();
        user.LastSignInAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_advance_the_version_on_every_recorded_attempt()
    {
        var user = NewUser();

        user.RecordFailedSignIn(_clock);
        user.RecordSuccessfulSignIn(_clock);

        user.Version.ShouldBe(AggregateRoot.InitialVersion + 2);
    }
}

[Trait("Rule", "BR-IDN-004")]
public sealed class UserAccessLevelTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private User NewUser(AccessLevel level = AccessLevel.Collaborator) =>
        User.Register(EmailAddress.Create("ana.souza@example.com").Value, "hash", level, _clock);

    [Fact]
    public void Should_change_the_level_bump_the_token_version_and_raise_an_event()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        var result = user.ChangeAccessLevel(AccessLevel.Manager, _clock);

        result.IsSuccess.ShouldBeTrue();
        user.AccessLevel.ShouldBe(AccessLevel.Manager);
        user.TokenVersion.ShouldBe(2);
        var domainEvent = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserAccessLevelChanged>();
        domainEvent.From.ShouldBe(AccessLevel.Collaborator);
        domainEvent.To.ShouldBe(AccessLevel.Manager);
        domainEvent.AggregateVersion.ShouldBe(user.Version);
    }

    [Fact]
    public void Should_do_nothing_when_the_level_is_unchanged()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var versionBefore = user.Version;

        var result = user.ChangeAccessLevel(AccessLevel.Collaborator, _clock);

        result.IsSuccess.ShouldBeTrue();
        user.TokenVersion.ShouldBe(1);
        user.Version.ShouldBe(versionBefore);
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_reject_an_undefined_level_and_leave_the_account_untouched()
    {
        var user = NewUser();

        var result = user.ChangeAccessLevel((AccessLevel)77, _clock);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INVALID");
        user.AccessLevel.ShouldBe(AccessLevel.Collaborator);
        user.TokenVersion.ShouldBe(1);
    }
}

[Trait("Rule", "BR-IDN-006")]
public sealed class UserDeactivationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private User NewUser() =>
        User.Register(EmailAddress.Create("ana.souza@example.com").Value, "hash", AccessLevel.Collaborator, _clock);

    [Fact]
    public void Should_deactivate_bump_the_token_version_and_stop_authenticating()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        user.Deactivate(_clock);

        user.Status.ShouldBe(UserStatus.Deactivated);
        user.TokenVersion.ShouldBe(2);
        user.CanSignIn(_clock.GetUtcNow()).ShouldBeFalse();
        user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserDeactivated>();
    }

    [Fact]
    public void Should_be_idempotent()
    {
        var user = NewUser();
        user.Deactivate(_clock);
        user.ClearDomainEvents();

        var result = user.Deactivate(_clock);

        result.IsSuccess.ShouldBeTrue();
        user.TokenVersion.ShouldBe(2);
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_invalidate_every_issued_token_when_revoking_all()
    {
        var user = NewUser();

        user.RevokeAllTokens(_clock);

        user.TokenVersion.ShouldBe(2);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Should_replace_the_password_hash_without_touching_the_token_version()
    {
        var user = NewUser();

        user.ReplacePasswordHash("stronger-hash", _clock);

        user.PasswordHash.ShouldBe("stronger-hash");
        user.TokenVersion.ShouldBe(1);
    }
}
