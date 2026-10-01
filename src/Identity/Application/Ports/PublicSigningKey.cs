namespace Portfolio.Identity.Application;

/// <summary>A public signing key in JWK form (RFC 7517).</summary>
/// <param name="KeyType">Key type, <c>EC</c>.</param>
/// <param name="Curve">Curve, <c>P-384</c>.</param>
/// <param name="X">Base64url X coordinate.</param>
/// <param name="Y">Base64url Y coordinate.</param>
/// <param name="KeyId">The <c>kid</c> referenced by token headers.</param>
/// <param name="Use">Always <c>sig</c>.</param>
/// <param name="Algorithm">Always <c>ES384</c>.</param>
internal sealed record PublicSigningKey(
    string KeyType,
    string Curve,
    string X,
    string Y,
    string KeyId,
    string Use,
    string Algorithm
);
