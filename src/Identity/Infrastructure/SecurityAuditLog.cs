using System.Diagnostics.Metrics;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Writes the security events of ai/SECURITY.md §11.1 as structured logs with stable event IDs, so alert rules
/// (≥ 5 failures in 5 minutes, refresh-token reuse, privilege changes) can key on them. Identifiers only:
/// never passwords, tokens, e-mail addresses, or other personal data (§11.2).
/// </summary>
internal sealed partial class SecurityAuditLog(ILogger<SecurityAuditLog> logger, IMeterFactory meterFactory)
    : ISecurityAudit
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "identity");

    private readonly Meter _meter = meterFactory.Create(TelemetryNames.ContextMeter("Identity"));

    // Counted next to the audit log, from the same call: the sign-in outcome is the Tier 1 journey's success signal. The
    // account is never a dimension (ai/OBSERVABILITY.md §5.4); the audit log carries the identifier.
    private Counter<long>? _signIns;
    private Counter<long>? _reuse;

    private Counter<long> SignIns =>
        _signIns ??= _meter.CreateCounter<long>(
            "identity.signins",
            unit: "{signin}",
            description: "Sign-in attempts by outcome: succeeded, failed, or locked (refused because the account is locked)."
        );

    private Counter<long> Reuse =>
        _reuse ??= _meter.CreateCounter<long>(
            "identity.refresh_token.reuse_detected",
            unit: "{event}",
            description: "Replayed refresh tokens detected: each one revoked a whole session."
        );

    /// <inheritdoc />
    public void SignInSucceeded(Guid userId)
    {
        SignIns.Add(1, Context, new KeyValuePair<string, object?>(TelemetryNames.Outcome, "succeeded"));
        LogSignInSucceeded(userId);
    }

    /// <inheritdoc />
    public void SignInFailed(Guid? userId, bool lockedOut)
    {
        SignIns.Add(
            1,
            Context,
            new KeyValuePair<string, object?>(TelemetryNames.Outcome, lockedOut ? "locked" : "failed")
        );
        LogSignInFailed(userId, lockedOut);
    }

    /// <inheritdoc />
    public void AccountLocked(Guid userId) => LogAccountLocked(userId);

    /// <inheritdoc />
    public void RefreshTokenReuseDetected(Guid userId, Guid familyId)
    {
        Reuse.Add(1, Context);
        LogRefreshTokenReuse(userId, familyId);
    }

    /// <inheritdoc />
    public void TokenRevoked(Guid? userId) => LogTokenRevoked(userId);

    /// <inheritdoc />
    public void AccountCreated(Guid userId, AccessLevel level, Guid? actorId) =>
        LogAccountCreated(userId, level, actorId);

    /// <inheritdoc />
    public void AccessLevelChanged(Guid actorId, Guid targetId, AccessLevel from, AccessLevel to) =>
        LogAccessLevelChanged(actorId, targetId, from, to);

    /// <inheritdoc />
    public void AccountDeactivated(Guid actorId, Guid targetId) => LogAccountDeactivated(actorId, targetId);

    /// <inheritdoc />
    public void PrivilegeDenied(Guid actorId, string reasonCode) => LogPrivilegeDenied(actorId, reasonCode);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "auth.sign_in.succeeded user={UserId}")]
    private partial void LogSignInSucceeded(Guid userId);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "auth.sign_in.failed user={UserId} locked_out={LockedOut}"
    )]
    private partial void LogSignInFailed(Guid? userId, bool lockedOut);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "auth.account.locked user={UserId}")]
    private partial void LogAccountLocked(Guid userId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Critical,
        Message = "auth.refresh_token.reuse_detected user={UserId} family={FamilyId}"
    )]
    private partial void LogRefreshTokenReuse(Guid userId, Guid familyId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "auth.token.revoked user={UserId}")]
    private partial void LogTokenRevoked(Guid? userId);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "auth.account.created user={UserId} level={Level} actor={ActorId}"
    )]
    private partial void LogAccountCreated(Guid userId, AccessLevel level, Guid? actorId);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "auth.access_level.changed actor={ActorId} target={TargetId} from={From} to={To}"
    )]
    private partial void LogAccessLevelChanged(Guid actorId, Guid targetId, AccessLevel from, AccessLevel to);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "auth.account.deactivated actor={ActorId} target={TargetId}"
    )]
    private partial void LogAccountDeactivated(Guid actorId, Guid targetId);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Warning,
        Message = "auth.privilege.denied actor={ActorId} reason={Reason}"
    )]
    private partial void LogPrivilegeDenied(Guid actorId, string reason);
}
