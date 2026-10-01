namespace Portfolio.Identity.Application;

/// <summary>Credentials returned by a successful sign-in or refresh.</summary>
/// <param name="AccessToken">Compact JWT access token.</param>
/// <param name="ExpiresInSeconds">Seconds until the access token expires.</param>
/// <param name="RefreshToken">Opaque single-use refresh token.</param>
internal sealed record TokenPair(string AccessToken, int ExpiresInSeconds, string RefreshToken);
