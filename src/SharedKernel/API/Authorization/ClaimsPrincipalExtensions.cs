using System.Security.Claims;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>Reads the validated token claims of the caller.</summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>The account identifier, or <c>null</c> for anonymous or malformed principals.</summary>
    /// <param name="principal">Caller.</param>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirstValue(AccessClaims.Subject), out var id) && id != Guid.Empty
            ? id
            : null;
    }

    /// <summary>
    /// The access level of the caller. Anything missing or unrecognized is <see cref="AccessLevel.Public"/>:
    /// the claim can only raise privileges when it is an exact, known value.
    /// </summary>
    /// <param name="principal">Caller.</param>
    public static AccessLevel GetAccessLevel(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return AccessLevels.TryParseWireName(principal.FindFirstValue(AccessClaims.AccessLevel), out var level)
            ? level
            : AccessLevel.Public;
    }

    /// <summary>The token identifier (<c>jti</c>), or <c>null</c> when absent.</summary>
    /// <param name="principal">Caller.</param>
    public static string? GetTokenId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AccessClaims.TokenId);
    }
}
