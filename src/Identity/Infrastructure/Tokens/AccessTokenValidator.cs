using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// The checks that run after a token's signature and claims are valid (ai/SECURITY.md §7.3, §7.6): the
/// account must still exist and be active, the token version and level must match the server-side state, the
/// <c>jti</c> must not be blocklisted, and <c>iat</c> must not lie in the future. This is what makes logout,
/// deactivation, and privilege changes effective immediately instead of at token expiry.
/// </summary>
internal static class AccessTokenValidator
{
    private static readonly TimeSpan MaxIssuedAtSkew = TimeSpan.FromSeconds(30);

    /// <summary>Validates the principal of a token that passed cryptographic validation.</summary>
    /// <param name="context">Bearer validation context.</param>
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        var principal = context.Principal;
        var cancellationToken = context.HttpContext.RequestAborted;

        if (principal is null || !await IsAcceptableAsync(principal, services, cancellationToken))
        {
            context.Fail("The token is no longer valid.");
        }
    }

    private static async Task<bool> IsAcceptableAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var userId = principal.GetUserId();
        var tokenId = principal.GetTokenId();
        var timeProvider = services.GetRequiredService<TimeProvider>();

        if (
            userId is null
            || string.IsNullOrEmpty(tokenId)
            || !int.TryParse(
                principal.FindFirst(AccessClaims.TokenVersion)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version
            )
            || !AccessLevels.TryParseWireName(principal.FindFirst(AccessClaims.AccessLevel)?.Value, out var level)
            || IsIssuedInTheFuture(principal, timeProvider)
        )
        {
            return false;
        }

        var revoked = services.GetRequiredService<IRevokedTokenStore>();
        if (await revoked.IsRevokedAsync(tokenId, cancellationToken))
        {
            return false;
        }

        // Cache-aside with event-driven eviction: the database is only read on a miss or while Valkey is down.
        var account = await services.GetRequiredService<AccountStateCache>().GetAsync(userId.Value, cancellationToken);

        return account.Accepts(version, level);
    }

    private static bool IsIssuedInTheFuture(System.Security.Claims.ClaimsPrincipal principal, TimeProvider timeProvider)
    {
        if (
            !long.TryParse(
                principal.FindFirst("iat")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var issuedAt
            )
        )
        {
            return true; // iat is required.
        }

        return DateTimeOffset.FromUnixTimeSeconds(issuedAt) > timeProvider.GetUtcNow() + MaxIssuedAtSkew;
    }
}
