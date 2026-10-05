using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Cursor format <c>base64url(payload) "." base64url(HMAC-SHA256(purpose, payload))</c>. The purpose is part of the
/// authenticated data, so a cursor cannot be replayed against another collection, and the comparison is constant-time.
/// The payload is readable (a keyset position of public data), but a client cannot forge or alter one.
/// </summary>
internal sealed class HmacPageCursorCodec : IPageCursorCodec
{
    /// <summary>Upper bound on the size of a cursor accepted from a client.</summary>
    public const int MaxCursorLength = 512;

    private const char Separator = '.';

    private readonly byte[] _key;

    /// <summary>Creates the codec.</summary>
    /// <param name="options">Holds the signing key.</param>
    /// <param name="environmentAllowsEphemeralKey">Whether a missing key may be replaced by a throw-away one.</param>
    /// <param name="logger">Logger.</param>
    public HmacPageCursorCodec(
        IOptions<PageCursorOptions> options,
        bool environmentAllowsEphemeralKey,
        ILogger<HmacPageCursorCodec> logger
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _key = ResolveKey(options.Value.CursorKey, environmentAllowsEphemeralKey, logger);
    }

    /// <inheritdoc />
    public string Protect(string purpose, string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(payload);

        var body = Encoding.UTF8.GetBytes(payload);
        return WebEncoders.Base64UrlEncode(body) + Separator + WebEncoders.Base64UrlEncode(Sign(purpose, body));
    }

    /// <inheritdoc />
    public bool TryUnprotect(string purpose, string cursor, out string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        payload = string.Empty;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxCursorLength)
        {
            return false;
        }

        var parts = cursor.Split(Separator);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var body = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, Sign(purpose, body)))
            {
                return false;
            }

            payload = Encoding.UTF8.GetString(body);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private byte[] Sign(string purpose, byte[] body)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);

        // Length-prefix the purpose so ("ab", "c") and ("a", "bc") can never produce the same authenticated data.
        var data = new byte[sizeof(int) + purposeBytes.Length + body.Length];
        BitConverter.TryWriteBytes(data, purposeBytes.Length);
        purposeBytes.CopyTo(data, sizeof(int));
        body.CopyTo(data, sizeof(int) + purposeBytes.Length);

        return HMACSHA256.HashData(_key, data);
    }

    private static byte[] ResolveKey(string? configured, bool allowEphemeral, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!allowEphemeral)
            {
                throw new InvalidOperationException(
                    "Pagination:CursorKey is not configured. Inject a base64 key of at least 32 random bytes from the secrets manager."
                );
            }

            PageCursorLog.EphemeralKey(logger);
            return RandomNumberGenerator.GetBytes(64);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Pagination:CursorKey is not valid base64.", exception);
        }

        return key.Length >= PageCursorOptions.MinKeyBytes
            ? key
            : throw new InvalidOperationException("Pagination:CursorKey must decode to at least 32 bytes.");
    }
}
