using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Portfolio.Identity.Infrastructure;

namespace Portfolio.Identity;

/// <summary>Composition of the Identity context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class IdentityModule
{
    /// <summary>
    /// Registers authentication and the default-deny authorization baseline: endpoints without explicit
    /// metadata require an authenticated user; anonymous access must be declared with <c>[AllowAnonymous]</c>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services
            .AddAuthentication(UnconfiguredAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, UnconfiguredAuthenticationHandler>(
                UnconfiguredAuthenticationHandler.SchemeName,
                configureOptions: null
            );

        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
