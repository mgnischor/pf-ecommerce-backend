using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Opaque 256-bit refresh tokens from a CSPRNG, persisted only as HMAC-SHA3-512 under a server secret
/// (ai/SECURITY.md §7.2). A database leak therefore yields nothing usable, and the lookup needs no
/// comparison of secrets in application code.
/// </summary>
internal sealed class RefreshTokenCodec : IRefreshTokenCodec
{
    private const int TokenBytes = 32;

    private readonly byte[] _key;

    /// <summary>Creates the codec.</summary>
    /// <param name="options">Holds the hashing key.</param>
    /// <param name="environmentAllowsEphemeralKey">Whether a missing key may be replaced by a throw-away one.</param>
    /// <param name="logger">Logger.</param>
    public RefreshTokenCodec(
        IOptions<TokenSecretsOptions> options,
        bool environmentAllowsEphemeralKey,
        ILogger<RefreshTokenCodec> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (!HMACSHA3_512.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "HMAC-SHA3-512 is unavailable on this platform (needs OpenSSL 3 or Windows 11 CNG)."
            );
        }

        _key = ResolveKey(options.Value.TokenHashKey, environmentAllowsEphemeralKey, logger);
    }

    /// <inheritdoc />
    public GeneratedRefreshToken Generate()
    {
        var plain = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedRefreshToken(plain, Hash(plain));
    }

    /// <inheritdoc />
    public string Hash(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        return Base64UrlEncoder.Encode(HMACSHA3_512.HashData(_key, Encoding.UTF8.GetBytes(plain)));
    }

    private static byte[] ResolveKey(string? configured, bool allowEphemeral, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!allowEphemeral)
            {
                throw new InvalidOperationException(
                    "Identity:TokenHashKey is not configured. Inject a base64 key of at least 32 random bytes from the secrets manager."
                );
            }

            RefreshTokenCodecLog.EphemeralKey(logger);
            return RandomNumberGenerator.GetBytes(64);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Identity:TokenHashKey is not valid base64.", exception);
        }

        return key.Length >= TokenSecretsOptions.MinKeyBytes
            ? key
            : throw new InvalidOperationException("Identity:TokenHashKey must decode to at least 32 bytes.");
    }
}
