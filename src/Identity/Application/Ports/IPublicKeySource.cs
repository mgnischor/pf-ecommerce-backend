namespace Portfolio.Identity.Application;

/// <summary>Source of the issuer's public keys for the JWKS endpoint. Never exposes private material.</summary>
internal interface IPublicKeySource
{
    /// <summary>The public keys in rotation: the active one and the retired ones still needed for verification.</summary>
    IReadOnlyList<PublicSigningKey> GetPublicKeys();
}
