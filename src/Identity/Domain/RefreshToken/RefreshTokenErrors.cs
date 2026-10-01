using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Errors of the refresh-token state machine (BR-IDN-005).</summary>
internal static class RefreshTokenErrors
{
    /// <summary>Unknown, expired, or revoked token. Also the only thing a client is ever told.</summary>
    public static Error Invalid => IdentityErrors.InvalidRefreshToken;

    /// <summary>
    /// The token was already consumed. Internal signal only: the use case revokes the family and still
    /// answers the client with <see cref="Invalid"/>, so an attacker learns nothing.
    /// </summary>
    public static Error Reused => Error.Unauthorized("REFRESH_TOKEN_REUSED", "BR-IDN-005");
}
