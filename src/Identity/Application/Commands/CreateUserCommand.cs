namespace Portfolio.Identity.Application;

/// <summary>Request by an administrator to create a staff account.</summary>
/// <param name="ActorId">The caller's account.</param>
/// <param name="ActorLevel">The caller's validated access level.</param>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Initial password. Never logged or echoed.</param>
/// <param name="AccessLevel">Wire name of the level to grant, for example <c>manager</c>.</param>
internal sealed record CreateUserCommand(
    Guid ActorId,
    Portfolio.SharedKernel.Domain.AccessLevel ActorLevel,
    string? Email,
    string? Password,
    string? AccessLevel
);
