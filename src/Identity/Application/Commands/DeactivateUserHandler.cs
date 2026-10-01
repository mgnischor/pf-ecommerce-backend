using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Deactivates an account (BR-IDN-006): the account stops authenticating, every access token it holds is
/// invalidated by the token-version bump, and every refresh-token session is revoked. Idempotent.
/// </summary>
internal sealed class DeactivateUserHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Target and the caller's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> HandleAsync(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.ActorLevel.Satisfies(AccessManagementRules.ManagementLevel))
        {
            audit.PrivilegeDenied(command.ActorId, IdentityErrors.AccessLevelInsufficient.Code);
            return Result.Failure(IdentityErrors.AccessLevelInsufficient);
        }

        var target = await users.GetByIdAsync(command.TargetId, cancellationToken);
        if (target is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        var manage = AccessManagementRules.CanManage(command.ActorId, command.ActorLevel, target);
        if (manage.IsFailure)
        {
            audit.PrivilegeDenied(command.ActorId, manage.Error.Code);
            return manage;
        }

        var wasActive = target.Status == UserStatus.Active;
        target.Deactivate(timeProvider);
        users.Update(target);

        foreach (var token in await refreshTokens.ListByUserAsync(target.Id, cancellationToken))
        {
            token.Revoke(timeProvider);
            refreshTokens.Update(token);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (wasActive)
        {
            audit.AccountDeactivated(command.ActorId, target.Id);
        }

        return Result.Success();
    }
}
