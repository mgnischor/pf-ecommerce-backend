using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// BR-IDN-004: who may create accounts and change levels. Privileges only ever flow downward: nobody can
/// grant a level above their own, manage themselves, or manage an account that is not strictly below them
/// (developers may manage any other account).
/// </summary>
internal static class AccessManagementRules
{
    /// <summary>Lowest level allowed to manage accounts.</summary>
    public const AccessLevel ManagementLevel = AccessLevel.Administrator;

    /// <summary>Checks that the caller may create an account or assign <paramref name="target"/>.</summary>
    /// <param name="actor">Caller's level.</param>
    /// <param name="target">Level to grant.</param>
    public static Result CanGrant(AccessLevel actor, AccessLevel target)
    {
        if (!actor.Satisfies(ManagementLevel))
        {
            return Result.Failure(IdentityErrors.AccessLevelInsufficient);
        }

        return target > actor ? Result.Failure(IdentityErrors.AccessLevelEscalation) : Result.Success();
    }

    /// <summary>Checks that the caller may act on an existing account.</summary>
    /// <param name="actorId">Caller's account.</param>
    /// <param name="actor">Caller's level.</param>
    /// <param name="target">Account to act on.</param>
    public static Result CanManage(Guid actorId, AccessLevel actor, User target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!actor.Satisfies(ManagementLevel))
        {
            return Result.Failure(IdentityErrors.AccessLevelInsufficient);
        }

        if (target.Id == actorId)
        {
            return Result.Failure(IdentityErrors.UserNotManageable);
        }

        var allowed = actor == AccessLevel.Developer || target.AccessLevel < actor;
        return allowed ? Result.Success() : Result.Failure(IdentityErrors.UserNotManageable);
    }
}
