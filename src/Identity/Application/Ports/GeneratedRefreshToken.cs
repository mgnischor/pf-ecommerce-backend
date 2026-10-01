namespace Portfolio.Identity.Application;

/// <summary>A freshly generated refresh token in both forms.</summary>
/// <param name="Plain">Opaque value handed to the client once and never stored.</param>
/// <param name="Hash">Base64url HMAC-SHA3-512 persisted for lookup.</param>
internal sealed record GeneratedRefreshToken(string Plain, string Hash);
