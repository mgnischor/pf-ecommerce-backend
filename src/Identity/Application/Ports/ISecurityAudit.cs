using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Security events the platform must record (ai/SECURITY.md §11.1). Implementations log identifiers only:
/// never passwords, tokens, e-mail addresses, or other personal data (§11.2).
/// </summary>
internal interface ISecurityAudit
{
    /// <summary>A sign-in succeeded.</summary>
    /// <param name="userId">Account.</param>
    void SignInSucceeded(Guid userId);

    /// <summary>A sign-in failed. <paramref name="userId"/> is <c>null</c> when no such account exists.</summary>
    /// <param name="userId">Account, if any.</param>
    /// <param name="lockedOut">Whether the account was blocked at the time of the attempt.</param>
    void SignInFailed(Guid? userId, bool lockedOut);

    /// <summary>An account was locked after repeated failures.</summary>
    /// <param name="userId">Account.</param>
    void AccountLocked(Guid userId);

    /// <summary>A consumed refresh token was presented again: possible theft, the session was revoked.</summary>
    /// <param name="userId">Account.</param>
    /// <param name="familyId">Revoked session.</param>
    void RefreshTokenReuseDetected(Guid userId, Guid familyId);

    /// <summary>A session or access token was revoked on request.</summary>
    /// <param name="userId">Account, if known.</param>
    void TokenRevoked(Guid? userId);

    /// <summary>An account was created.</summary>
    /// <param name="userId">Account.</param>
    /// <param name="level">Initial level.</param>
    /// <param name="actorId">Administrator who created it, or <c>null</c> for self-registration.</param>
    void AccountCreated(Guid userId, AccessLevel level, Guid? actorId);

    /// <summary>A privilege change: audited with actor, target, and both levels.</summary>
    /// <param name="actorId">Who changed it.</param>
    /// <param name="targetId">Whose level changed.</param>
    /// <param name="from">Previous level.</param>
    /// <param name="to">New level.</param>
    void AccessLevelChanged(Guid actorId, Guid targetId, AccessLevel from, AccessLevel to);

    /// <summary>An account was deactivated and its tokens revoked.</summary>
    /// <param name="actorId">Who deactivated it.</param>
    /// <param name="targetId">The account.</param>
    void AccountDeactivated(Guid actorId, Guid targetId);

    /// <summary>A management action was refused for lack of privilege.</summary>
    /// <param name="actorId">Caller.</param>
    /// <param name="reasonCode">Stable error code.</param>
    void PrivilegeDenied(Guid actorId, string reasonCode);
}
