using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>Registers the access-level authorization policies and the default-deny baseline.</summary>
internal static class AccessLevelPolicies
{
    /// <summary>
    /// Adds one policy per level and the fallback policy that requires an authenticated caller for every
    /// endpoint without explicit metadata (OWASP A01).
    /// </summary>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddAccessLevelAuthorization(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, MinimumAccessLevelHandler>());

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AccessPolicies.Authenticated, policy => Require(policy, AccessLevel.Public))
            .AddPolicy(AccessPolicies.Collaborator, policy => Require(policy, AccessLevel.Collaborator))
            .AddPolicy(AccessPolicies.Manager, policy => Require(policy, AccessLevel.Manager))
            .AddPolicy(AccessPolicies.Administrator, policy => Require(policy, AccessLevel.Administrator))
            .AddPolicy(AccessPolicies.Developer, policy => Require(policy, AccessLevel.Developer))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static void Require(AuthorizationPolicyBuilder policy, AccessLevel level) =>
        policy.RequireAuthenticatedUser().AddRequirements(new MinimumAccessLevelRequirement(level));
}
