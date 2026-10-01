using Microsoft.AspNetCore.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>
/// Succeeds when the authenticated caller holds the required level. Never fails explicitly: an unmet
/// requirement is a plain denial (403), and the level claim is trusted only because the token was
/// validated and re-checked against the server-side account state.
/// </summary>
internal sealed class MinimumAccessLevelHandler : AuthorizationHandler<MinimumAccessLevelRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumAccessLevelRequirement requirement
    )
    {
        if (
            context.User.Identity?.IsAuthenticated == true
            && context.User.GetAccessLevel().Satisfies(requirement.Level)
        )
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
