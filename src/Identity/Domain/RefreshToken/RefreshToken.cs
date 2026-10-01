using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>
/// One link of a refresh-token family (BR-IDN-005). The opaque token itself is never stored, only its
/// HMAC-SHA3-512 (ai/SECURITY.md §7.2). Every use consumes the token and issues its successor in the same
/// family; presenting a consumed token again signals theft and the whole family is revoked.
/// </summary>
internal sealed class RefreshToken : AggregateRoot
{
    // 0 while unused, 1 once consumed. The compare-and-swap makes consumption atomic inside one process;
    // the persisted version token gives the same guarantee across instances.
    private int _consumed;

    /// <summary>Account the token belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Sign-in session the token belongs to; all tokens of a session share it.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>Base64url HMAC-SHA3-512 of the opaque token.</summary>
    public string TokenHash { get; private set; }

    /// <summary>UTC expiry of this token.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>UTC absolute expiry of the whole family: a session can never outlive it, however often it is refreshed.</summary>
    public DateTimeOffset FamilyExpiresAt { get; private set; }

    /// <summary>UTC instant the token was consumed, or <c>null</c> while unused.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>UTC instant the token was revoked, or <c>null</c>.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private RefreshToken() { }
#pragma warning restore CS8618

    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset familyExpiresAt,
        TimeProvider timeProvider
    )
        : base(id, timeProvider)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        FamilyExpiresAt = familyExpiresAt;
    }

    /// <summary>Issues a token of a family.</summary>
    /// <param name="userId">Account identifier.</param>
    /// <param name="familyId">Session identifier.</param>
    /// <param name="tokenHash">Base64url HMAC of the opaque token.</param>
    /// <param name="expiresAt">Expiry of this token; must not exceed <paramref name="familyExpiresAt"/>.</param>
    /// <param name="familyExpiresAt">Absolute expiry of the family.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static RefreshToken Issue(
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset familyExpiresAt,
        TimeProvider timeProvider
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (userId == Guid.Empty || familyId == Guid.Empty)
        {
            throw new ArgumentException("User and family identifiers must not be empty.", nameof(userId));
        }

        if (expiresAt > familyExpiresAt)
        {
            throw new ArgumentException("A token cannot outlive its family.", nameof(expiresAt));
        }

        return new RefreshToken(
            NewId(timeProvider),
            userId,
            familyId,
            tokenHash,
            expiresAt,
            familyExpiresAt,
            timeProvider
        );
    }

    /// <summary>
    /// Consumes the token (rotation). Fails with <see cref="RefreshTokenErrors.Reused"/> when it was
    /// already consumed, which the caller must treat as theft and answer by revoking the family.
    /// </summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Use(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var now = timeProvider.GetUtcNow();
        if (RevokedAt is not null || now >= ExpiresAt || now >= FamilyExpiresAt)
        {
            return Result.Failure(RefreshTokenErrors.Invalid);
        }

        if (UsedAt is not null || Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
        {
            return Result.Failure(RefreshTokenErrors.Reused);
        }

        UsedAt = now;
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    /// <summary>Revokes the token. Idempotent.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public void Revoke(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = timeProvider.GetUtcNow();
        MarkUpdated(timeProvider);
    }
}
