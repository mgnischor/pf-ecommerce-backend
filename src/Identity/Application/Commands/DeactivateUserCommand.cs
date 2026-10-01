namespace Portfolio.Identity.Application;

/// <summary>Request to deactivate an account and revoke its tokens.</summary>
/// <param name="ActorId">The caller's account.</param>
/// <param name="ActorLevel">The caller's validated access level.</param>
/// <param name="TargetId">Account to deactivate.</param>
internal sealed record DeactivateUserCommand(
    Guid ActorId,
    Portfolio.SharedKernel.Domain.AccessLevel ActorLevel,
    Guid TargetId
);
