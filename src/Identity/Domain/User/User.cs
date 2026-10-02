using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>
/// An account that can authenticate. Enforces BR-IDN-003 (temporary lockout after repeated failures),
/// BR-IDN-004 (access level), and BR-IDN-006 (deactivation and token revocation). The password is never
/// stored, only its Argon2id verifier in PHC string format (ai/SECURITY.md §4.2).
/// </summary>
internal sealed class User : AggregateRoot
{
    /// <summary>Consecutive failed sign-ins that trigger a lockout (BR-IDN-003).</summary>
    public const int MaxFailedSignIns = 5;

    /// <summary>How long an account stays locked after <see cref="MaxFailedSignIns"/> failures (BR-IDN-003).</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Normalized e-mail, unique among accounts (BR-IDN-001).</summary>
    public EmailAddress Email { get; private set; }

    /// <summary>Argon2id verifier in PHC string format. Critical data: never logged or exposed.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Access level of the account (BR-IDN-004).</summary>
    public AccessLevel AccessLevel { get; private set; }

    /// <summary>Lifecycle status.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>
    /// Version of the account's token set. Access tokens carry it; any token whose value differs from the
    /// current one is rejected, which revokes every outstanding token at once (BR-IDN-006).
    /// </summary>
    public int TokenVersion { get; private set; }

    /// <summary>Consecutive failed sign-ins since the last success or lockout.</summary>
    public int FailedSignIns { get; private set; }

    /// <summary>End of the current lockout, or <c>null</c> when the account is not locked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>UTC instant of the last successful sign-in.</summary>
    public DateTimeOffset? LastSignInAt { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private User() { }
#pragma warning restore CS8618

    private User(Guid id, EmailAddress email, string passwordHash, AccessLevel level, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        Email = email;
        PasswordHash = passwordHash;
        AccessLevel = level;
        Status = UserStatus.Active;
        TokenVersion = 1;
    }

    /// <summary>Creates an active account.</summary>
    /// <param name="email">Normalized e-mail.</param>
    /// <param name="passwordHash">Argon2id PHC string produced by the password hasher.</param>
    /// <param name="level">Initial access level; the caller has already checked it may grant it.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static User Register(EmailAddress email, string passwordHash, AccessLevel level, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Undefined access level.");
        }

        var user = new User(NewId(timeProvider), email, passwordHash, level, timeProvider);
        user.AddDomainEvent(UserRegistered.For(user));
        return user;
    }

    /// <summary>Whether the account is locked at <paramref name="now"/> (BR-IDN-003).</summary>
    /// <param name="now">Current UTC instant.</param>
    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && now < until;

    /// <summary>Whether the account may authenticate at <paramref name="now"/>.</summary>
    /// <param name="now">Current UTC instant.</param>
    public bool CanSignIn(DateTimeOffset now) => Status == UserStatus.Active && !IsLockedOut(now);

    /// <summary>Counts a failed sign-in and locks the account after <see cref="MaxFailedSignIns"/> (BR-IDN-003).</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public void RecordFailedSignIn(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        FailedSignIns++;
        if (FailedSignIns >= MaxFailedSignIns)
        {
            LockedUntil = timeProvider.GetUtcNow().Add(LockoutDuration);
            FailedSignIns = 0;
        }

        MarkUpdated(timeProvider);
    }

    /// <summary>Clears the failure counter and any expired lockout after a successful sign-in.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public void RecordSuccessfulSignIn(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        FailedSignIns = 0;
        LockedUntil = null;
        LastSignInAt = timeProvider.GetUtcNow();
        MarkUpdated(timeProvider);
    }

    /// <summary>Stores a stronger verifier computed after a successful sign-in (transparent upgrade, ai/SECURITY.md §4.2).</summary>
    /// <param name="passwordHash">New Argon2id PHC string.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public void ReplacePasswordHash(string passwordHash, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(timeProvider);

        PasswordHash = passwordHash;
        MarkUpdated(timeProvider);
    }

    /// <summary>
    /// Changes the access level (BR-IDN-004). Bumps the token version so tokens that carry the old level
    /// stop working immediately; raises <see cref="UserAccessLevelChanged"/> when the level differs.
    /// </summary>
    /// <param name="level">New level; the caller has already checked it may grant it.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result ChangeAccessLevel(AccessLevel level, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!Enum.IsDefined(level))
        {
            return Result.Failure(IdentityErrors.AccessLevelInvalid);
        }

        if (level == AccessLevel)
        {
            return Result.Success();
        }

        var from = AccessLevel;
        AccessLevel = level;
        TokenVersion++;
        MarkUpdated(timeProvider);
        AddDomainEvent(UserAccessLevelChanged.For(this, from));
        return Result.Success();
    }

    /// <summary>Deactivates the account and revokes every token it holds (BR-IDN-006). Idempotent.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Deactivate(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status == UserStatus.Deactivated)
        {
            return Result.Success();
        }

        Status = UserStatus.Deactivated;
        TokenVersion++;
        MarkUpdated(timeProvider);
        AddDomainEvent(UserDeactivated.For(this));
        return Result.Success();
    }

    /// <summary>Invalidates every access token issued so far (refresh-token reuse, suspected compromise).</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public void RevokeAllTokens(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        TokenVersion++;
        MarkUpdated(timeProvider);
        AddDomainEvent(UserTokensRevoked.For(this));
    }
}
