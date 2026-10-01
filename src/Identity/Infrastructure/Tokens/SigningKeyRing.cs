using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// The ES384 (ECDSA P-384) keys of the issuer. Resolves verification keys strictly by <c>kid</c> (never by
/// trying every key), publishes the public halves as a JWKS, and supports rotation: the active key signs,
/// retired keys keep verifying (ai/SECURITY.md §7.1, §7.6).
/// </summary>
internal sealed class SigningKeyRing : IDisposable
{
    /// <summary>JOSE algorithm of every key.</summary>
    public const string Algorithm = SecurityAlgorithms.EcdsaSha384;

    private const int P384KeySize = 384;

    private readonly Dictionary<string, ECDsaSecurityKey> _verificationKeys = new(StringComparer.Ordinal);
    private readonly List<ECDsa> _owned = [];

    private SigningKeyRing(SigningCredentials signing) => Signing = signing;

    /// <summary>Credentials that sign new tokens.</summary>
    public SigningCredentials Signing { get; }

    /// <summary>Builds the ring from configuration.</summary>
    /// <param name="options">Validated JWT options.</param>
    /// <param name="allowEphemeralKey">Whether a missing key may be replaced by a throw-away one (Development only).</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="InvalidOperationException">Thrown when no usable key exists and an ephemeral key is not allowed.</exception>
    public static SigningKeyRing Create(JwtOptions options, bool allowEphemeralKey, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.Keys.Count == 0)
        {
            if (!allowEphemeralKey)
            {
                throw new InvalidOperationException(
                    "No JWT signing key is configured. Inject Jwt:Keys and Jwt:ActiveKeyId from the secrets manager."
                );
            }

            SigningKeyLog.EphemeralKey(logger);
            var generated = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            return FromKeys(
                [new KeyMaterial("ephemeral-" + Guid.NewGuid().ToString("N")[..8], generated, IsPrivate: true)],
                activeId: null,
                [generated]
            );
        }

        List<KeyMaterial> keys = [];
        List<ECDsa> owned = [];
        foreach (var key in options.Keys)
        {
            var ecdsa = ECDsa.Create();
            owned.Add(ecdsa);
            var isPrivate = LoadKey(ecdsa, key);
            keys.Add(new KeyMaterial(key.Id, ecdsa, isPrivate));
        }

        return FromKeys(keys, options.ActiveKeyId, owned);
    }

    /// <summary>Resolves the verification key of a token header, or nothing when the <c>kid</c> is unknown.</summary>
    /// <param name="keyId">The <c>kid</c> header value.</param>
    public IEnumerable<SecurityKey> Resolve(string? keyId) =>
        keyId is not null && _verificationKeys.TryGetValue(keyId, out var key) ? [key] : [];

    /// <summary>The public keys as a JSON Web Key Set, built once. Never contains private parameters.</summary>
    public JsonWebKeySet PublicKeys { get; private set; } = new();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var ecdsa in _owned)
        {
            ecdsa.Dispose();
        }
    }

    private static SigningKeyRing FromKeys(List<KeyMaterial> keys, string? activeId, List<ECDsa> owned)
    {
        var active = activeId is null
            ? keys[0]
            : keys.First(key => string.Equals(key.Id, activeId, StringComparison.Ordinal));
        if (!active.IsPrivate)
        {
            throw new InvalidOperationException("The active JWT signing key has no private part.");
        }

        var signing = new SigningCredentials(new ECDsaSecurityKey(active.Key) { KeyId = active.Id }, Algorithm);
        var ring = new SigningKeyRing(signing);
        ring._owned.AddRange(owned);

        foreach (var key in keys)
        {
            ring._verificationKeys[key.Id] = new ECDsaSecurityKey(key.Key) { KeyId = key.Id };

            // Export without private parameters; the converter copies the values, so the temporary key can go.
            using var publicOnly = ECDsa.Create(key.Key.ExportParameters(includePrivateParameters: false));
            var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(
                new ECDsaSecurityKey(publicOnly) { KeyId = key.Id }
            );
            jwk.Use = "sig";
            jwk.Alg = Algorithm;
            ring.PublicKeys.Keys.Add(jwk);
        }

        return ring;
    }

    private static bool LoadKey(ECDsa ecdsa, JwtKeyOptions key)
    {
        var privatePem = key.PrivateKeyPemFile is { Length: > 0 } file ? File.ReadAllText(file) : key.PrivateKeyPem;
        bool isPrivate;

        if (!string.IsNullOrWhiteSpace(privatePem))
        {
            ecdsa.ImportFromPem(privatePem);
            isPrivate = true;
        }
        else if (!string.IsNullOrWhiteSpace(key.PublicKeyPem))
        {
            ecdsa.ImportFromPem(key.PublicKeyPem);
            isPrivate = false;
        }
        else
        {
            throw new InvalidOperationException($"JWT key '{key.Id}' has no key material.");
        }

        if (ecdsa.KeySize != P384KeySize)
        {
            throw new InvalidOperationException($"JWT key '{key.Id}' must be an ECDSA P-384 key (ES384).");
        }

        return isPrivate;
    }

    private sealed record KeyMaterial(string Id, ECDsa Key, bool IsPrivate);
}
