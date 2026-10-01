namespace Portfolio.Identity.Application;

/// <summary>Request to end a session (sign out).</summary>
/// <param name="RefreshToken">The session's refresh token, if the client has it.</param>
/// <param name="AccessTokenId">The <c>jti</c> of the caller's current access token, if authenticated.</param>
/// <param name="AccessTokenExpiresAt">Expiry of that access token.</param>
/// <param name="UserId">The authenticated account, if any.</param>
internal sealed record RevokeTokenCommand(
    string? RefreshToken,
    string? AccessTokenId,
    DateTimeOffset? AccessTokenExpiresAt,
    Guid? UserId
);
