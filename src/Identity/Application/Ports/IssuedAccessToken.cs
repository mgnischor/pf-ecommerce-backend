namespace Portfolio.Identity.Application;

/// <summary>A signed access token and the data the use case needs to describe it.</summary>
/// <param name="Token">The compact JWT.</param>
/// <param name="TokenId">The <c>jti</c> claim.</param>
/// <param name="ExpiresAt">UTC expiry.</param>
internal sealed record IssuedAccessToken(string Token, string TokenId, DateTimeOffset ExpiresAt);
