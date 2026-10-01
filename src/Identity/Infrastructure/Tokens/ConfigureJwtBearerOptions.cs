using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portfolio.SharedKernel.API.Authorization;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Configures bearer validation exactly as ai/SECURITY.md §7.1 and §7.3 prescribe: explicit algorithm and
/// <c>typ</c> allowlists, exact issuer and audience, mandatory signature and expiry, a 30-second skew, key
/// lookup by <c>kid</c> only, raw claim names, and tokens accepted from the <c>Authorization</c> header only.
/// </summary>
internal sealed class ConfigureJwtBearerOptions(
    SigningKeyRing keys,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider
) : IConfigureNamedOptions<JwtBearerOptions>
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var settings = jwtOptions.Value;

        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.RequireHttpsMetadata = true;
        options.IncludeErrorDetails = false; // Failures answer a bare "Bearer" challenge: nothing to learn from.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SigningKeyRing.Algorithm],
            ValidTypes = [JwtAccessTokenIssuer.TokenType],
            ClockSkew = ClockSkew,
            LifetimeValidator = ValidateLifetime,
            NameClaimType = AccessClaims.Subject,
            IssuerSigningKeyResolver = (_, _, keyId, _) => keys.Resolve(keyId),
        };

        options.Events = new JwtBearerEvents { OnTokenValidated = AccessTokenValidator.ValidateAsync };
    }

    /// <inheritdoc />
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    // Lifetime is validated against the injected clock (not the system clock) so expiry is testable and
    // consistent with every other time-dependent rule; exp is mandatory, nbf is checked when present.
    private bool ValidateLifetime(
        DateTime? notBefore,
        DateTime? expires,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters
    )
    {
        if (expires is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var skewedNotBefore = notBefore is null || notBefore.Value <= now + ClockSkew;
        return skewedNotBefore && expires.Value + ClockSkew > now;
    }
}
