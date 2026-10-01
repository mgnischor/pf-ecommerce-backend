namespace Portfolio.Identity.Application;

/// <summary>Request to rotate a refresh token into a new token pair.</summary>
/// <param name="RefreshToken">The opaque refresh token. Never logged or echoed.</param>
internal sealed record RefreshTokenCommand(string? RefreshToken);
