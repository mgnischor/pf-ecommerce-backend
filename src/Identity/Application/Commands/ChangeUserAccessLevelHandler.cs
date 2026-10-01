using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Changes an account's access level (BR-IDN-004). Checks the caller against the server-side state of the
/// target, not against anything the client sent, and audits every change as a privilege change.
/// </summary>
internal sealed class ChangeUserAccessLevelHandler(
    IUserRepository users,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">New level and the caller's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> HandleAsync(ChangeUserAccessLevelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AccessLevels.TryParseWireName(command.AccessLevel, out var level))
        {
            return Result.Failure(IdentityErrors.AccessLevelInvalid);
        }

        var grant = AccessManagementRules.CanGrant(command.ActorLevel, level);
        if (grant.IsFailure)
        {
            return Denied(command.ActorId, grant);
        }

        var target = await users.GetByIdAsync(command.TargetId, cancellationToken);
        if (target is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        var manage = AccessManagementRules.CanManage(command.ActorId, command.ActorLevel, target);
        if (manage.IsFailure)
        {
            return Denied(command.ActorId, manage);
        }

        var previous = target.AccessLevel;
        var result = target.ChangeAccessLevel(level, timeProvider);
        if (result.IsFailure)
        {
            return result;
        }

        users.Update(target);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (previous != level)
        {
            audit.AccessLevelChanged(command.ActorId, target.Id, previous, level);
        }

        return result;
    }

    private Result Denied(Guid actorId, Result failure)
    {
        audit.PrivilegeDenied(actorId, failure.Error!.Code);
        return failure;
    }
}
