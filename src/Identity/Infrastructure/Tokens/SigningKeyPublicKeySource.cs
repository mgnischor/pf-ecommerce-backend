using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>Adapts the <see cref="SigningKeyRing"/> to the <see cref="IPublicKeySource"/> port.</summary>
internal sealed class SigningKeyPublicKeySource(SigningKeyRing keys) : IPublicKeySource
{
    /// <inheritdoc />
    public IReadOnlyList<PublicSigningKey> GetPublicKeys() =>
        [
            .. keys.PublicKeys.Keys.Select(key => new PublicSigningKey(
                key.Kty,
                key.Crv,
                key.X,
                key.Y,
                key.Kid,
                key.Use,
                key.Alg
            )),
        ];
}
