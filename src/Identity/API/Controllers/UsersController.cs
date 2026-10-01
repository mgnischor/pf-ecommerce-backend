using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Identity.API.Contracts;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.API.Controllers;

/// <summary>
/// Account administration (administrator level and above). Privileges only flow downward (BR-IDN-004): nobody
/// can grant a level above their own, manage themselves, or manage an account not strictly below them
/// (developers may manage any other account). The rules are enforced by the use cases against server-side state.
/// </summary>
[Route("api/v1/users")]
[Authorize(Policy = AccessPolicies.Administrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class UsersController(
    CreateUserHandler create,
    ChangeUserAccessLevelHandler changeLevel,
    DeactivateUserHandler deactivate
) : ApiControllerBase
{
    /// <summary>Creates a staff account at a level not above the caller's own.</summary>
    /// <param name="request">Account data.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } actor)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new CreateUserCommand(
            actor,
            CurrentAccessLevel,
            request.Email,
            request.Password,
            request.AccessLevel
        );
        var result = await create.HandleAsync(command, cancellationToken);

        return result.IsFailure
            ? ProblemFrom(result.Error)
            : StatusCode(StatusCodes.Status201Created, new UserResponse(result.Value, request.AccessLevel));
    }

    /// <summary>Changes an account's access level. Tokens carrying the old level stop working immediately.</summary>
    /// <param name="userId">Account to change.</param>
    /// <param name="request">New level.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPut("{userId:guid}/access-level")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ChangeAccessLevel(
        Guid userId,
        [FromBody] ChangeAccessLevelRequest request,
        CancellationToken cancellationToken
    )
    {
        if (CurrentUserId is not { } actor)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new ChangeUserAccessLevelCommand(actor, CurrentAccessLevel, userId, request.AccessLevel);
        var result = await changeLevel.HandleAsync(command, cancellationToken);

        return result.IsFailure ? ProblemFrom(result.Error) : NoContent();
    }

    /// <summary>Deactivates an account: it stops authenticating and every token it holds is revoked. Idempotent.</summary>
    /// <param name="userId">Account to deactivate.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{userId:guid}/deactivation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } actor)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var result = await deactivate.HandleAsync(
            new DeactivateUserCommand(actor, CurrentAccessLevel, userId),
            cancellationToken
        );
        return result.IsFailure ? ProblemFrom(result.Error) : NoContent();
    }
}
