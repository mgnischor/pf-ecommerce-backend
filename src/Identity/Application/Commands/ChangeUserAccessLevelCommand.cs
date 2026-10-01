namespace Portfolio.Identity.Application;

/// <summary>Request to change an account's access level.</summary>
/// <param name="ActorId">The caller's account.</param>
/// <param name="ActorLevel">The caller's validated access level.</param>
/// <param name="TargetId">Account to change.</param>
/// <param name="AccessLevel">Wire name of the new level.</param>
internal sealed record ChangeUserAccessLevelCommand(
    Guid ActorId,
    Portfolio.SharedKernel.Domain.AccessLevel ActorLevel,
    Guid TargetId,
    string? AccessLevel
);
