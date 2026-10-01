namespace Portfolio.Identity.Infrastructure;

/// <summary>One signing key (ES384, P-384). Exactly one source of key material must be set.</summary>
internal sealed class JwtKeyOptions
{
    /// <summary>The <c>kid</c> written to token headers and to the JWKS.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>PEM of the private key (PKCS#8). Secret: inject through the secrets manager.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Path of a mounted file with the private key PEM (preferred over an inline value).</summary>
    public string? PrivateKeyPemFile { get; set; }

    /// <summary>PEM of a public key, for retired keys that can only verify.</summary>
    public string? PublicKeyPem { get; set; }
}
