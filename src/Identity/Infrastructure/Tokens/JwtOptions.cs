namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// JWT settings (<c>Jwt</c> section). Lifetimes and names are plain configuration; key material is a secret
/// and must be injected at runtime (environment variables or mounted files), never committed
/// (ai/SECURITY.md §5). Limits follow ai/SECURITY.md §7.2 and are enforced by <see cref="JwtOptionsValidator"/>.
/// </summary>
internal sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Maximum access-token lifetime allowed by the standard.</summary>
    public static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Maximum lifetime of one refresh token.</summary>
    public static readonly TimeSpan MaxRefreshTokenLifetime = TimeSpan.FromDays(7);

    /// <summary>Maximum absolute lifetime of a refresh-token family (sign-in session).</summary>
    public static readonly TimeSpan MaxRefreshFamilyLifetime = TimeSpan.FromDays(30);

    /// <summary>Exact <c>iss</c> of issued and accepted tokens.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Exact <c>aud</c> of issued and accepted tokens (this API).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Access-token lifetime (default 10 minutes; at most 15).</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Refresh-token lifetime (default 7 days).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Absolute session lifetime (default 30 days).</summary>
    public TimeSpan RefreshFamilyLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Identifier of the key that signs new tokens. Required when keys are configured.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// Signing keys. The active one must carry a private key; retired ones may be public-only and are kept
    /// for the maximum access-token lifetime after a rotation so tokens issued before it still validate.
    /// </summary>
    public List<JwtKeyOptions> Keys { get; set; } = [];
}
