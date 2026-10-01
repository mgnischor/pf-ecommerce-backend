using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Portfolio.IntegrationTests.Http;

/// <summary>Parameters of a forged access token; each test changes exactly one to prove it is rejected.</summary>
internal sealed record ForgedToken
{
    public required ECDsa Key { get; init; }
    public string KeyId { get; init; } = ApiFactory.KeyId;
    public string Issuer { get; init; } = ApiFactory.Issuer;
    public string Audience { get; init; } = ApiFactory.Audience;
    public string Type { get; init; } = "at+jwt";
    public required string Subject { get; init; }
    public required int Version { get; init; }
    public required string Level { get; init; }
    public string? TokenId { get; init; } = Guid.NewGuid().ToString("N");
    public TimeSpan IssuedAtOffset { get; init; } = TimeSpan.Zero;
    public TimeSpan NotBeforeOffset { get; init; } = TimeSpan.Zero;
    public TimeSpan ExpiresOffset { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>Builds access tokens, valid or deliberately defective, signed with the test key.</summary>
internal static class TokenForge
{
    public static string Create(ForgedToken token)
    {
        var now = TimeProvider.System.GetUtcNow();
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sub"] = token.Subject,
            ["ver"] = token.Version,
            ["access_level"] = token.Level,
        };
        if (token.TokenId is not null)
        {
            claims["jti"] = token.TokenId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = token.Issuer,
            Audience = token.Audience,
            IssuedAt = (now + token.IssuedAtOffset).UtcDateTime,
            NotBefore = (now + token.NotBeforeOffset).UtcDateTime,
            Expires = (now + token.ExpiresOffset).UtcDateTime,
            TokenType = token.Type,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(token.Key) { KeyId = token.KeyId },
                SecurityAlgorithms.EcdsaSha384
            ),
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>A token with <c>alg: none</c> and no signature.</summary>
    public static string Unsigned(ForgedToken token)
    {
        var signed = new JsonWebTokenHandler().ReadJsonWebToken(Create(token));
        var header = Base64UrlEncoder.Encode(
            JsonSerializer.Serialize(
                new
                {
                    alg = "none",
                    typ = "at+jwt",
                    kid = token.KeyId,
                }
            )
        );
        return header + "." + signed.EncodedPayload + ".";
    }

    /// <summary>
    /// A token signed with HS256 using the published public key as the secret: the classic algorithm-confusion
    /// attack against verifiers that trust the <c>alg</c> header.
    /// </summary>
    public static string HmacWithPublicKey(ForgedToken token)
    {
        var now = TimeProvider.System.GetUtcNow();
        var secret = Encoding.UTF8.GetBytes(token.Key.ExportSubjectPublicKeyInfoPem());
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = token.Issuer,
            Audience = token.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + token.ExpiresOffset).UtcDateTime,
            TokenType = token.Type,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = token.Subject,
                ["ver"] = token.Version,
                ["access_level"] = token.Level,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(secret) { KeyId = token.KeyId },
                SecurityAlgorithms.HmacSha256
            ),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Replaces the payload of a real token, keeping its original signature.</summary>
    public static string TamperPayload(string realToken, string claim, string value)
    {
        var parts = realToken.Split('.');
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        var changed = payload
            .RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object)p.Value.Clone(), StringComparer.Ordinal);
        changed[claim] = value;
        return parts[0] + "." + Base64UrlEncoder.Encode(JsonSerializer.Serialize(changed)) + "." + parts[2];
    }

    /// <summary>Reads the claims of a real token without validating it.</summary>
    public static (string Subject, int Version, string Level) Read(string token)
    {
        var parsed = new JsonWebTokenHandler().ReadJsonWebToken(token);
        return (
            parsed.GetClaim("sub").Value,
            int.Parse(parsed.GetClaim("ver").Value, System.Globalization.CultureInfo.InvariantCulture),
            parsed.GetClaim("access_level").Value
        );
    }

    /// <summary>Creates an unrelated key pair, to sign tokens the server must not trust.</summary>
    public static ECDsa NewAttackerKey() => ECDsa.Create(ECCurve.NamedCurves.nistP384);
}
