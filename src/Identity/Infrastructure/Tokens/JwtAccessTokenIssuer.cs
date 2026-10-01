using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Issues ES384-signed access tokens with <c>typ: at+jwt</c> (ai/SECURITY.md §7). The payload carries only
/// opaque identifiers, the access level, and the token version: no e-mail or other personal data.
/// </summary>
internal sealed class JwtAccessTokenIssuer(SigningKeyRing keys, IOptions<JwtOptions> options, TimeProvider timeProvider)
    : IAccessTokenIssuer
{
    /// <summary>Value of the <c>typ</c> header of access tokens.</summary>
    public const string TokenType = "at+jwt";

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    /// <inheritdoc />
    public IssuedAccessToken Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + settings.AccessTokenLifetime;
        var tokenId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            TokenType = TokenType,
            SigningCredentials = keys.Signing,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AccessClaims.Subject] = user.Id.ToString("D", CultureInfo.InvariantCulture),
                [AccessClaims.TokenId] = tokenId,
                [AccessClaims.AccessLevel] = user.AccessLevel.ToWireName(),
                [AccessClaims.TokenVersion] = user.TokenVersion,
            },
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), tokenId, expiresAt);
    }
}
