namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Secret used to hash refresh tokens before they are stored (<c>Identity:TokenHashKey</c>). Inject it from
/// the secrets manager; it is never committed. Development may run without it (an ephemeral key is used).
/// </summary>
internal sealed class TokenSecretsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Identity";

    /// <summary>Minimum key length in bytes (512-bit HMAC output, so at least 64 random bytes are advised).</summary>
    public const int MinKeyBytes = 32;

    /// <summary>Base64 key of at least <see cref="MinKeyBytes"/> random bytes.</summary>
    public string? TokenHashKey { get; set; }
}
