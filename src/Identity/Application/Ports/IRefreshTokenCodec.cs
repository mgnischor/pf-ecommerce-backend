namespace Portfolio.Identity.Application;

/// <summary>Generates refresh tokens and derives their persisted hash (ai/SECURITY.md §7.2).</summary>
internal interface IRefreshTokenCodec
{
    /// <summary>Generates a 256-bit opaque token from a CSPRNG.</summary>
    GeneratedRefreshToken Generate();

    /// <summary>Computes the persisted hash of a token presented by a client.</summary>
    /// <param name="plain">Opaque token.</param>
    string Hash(string plain);
}
