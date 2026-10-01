using Microsoft.Extensions.Options;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Validates <see cref="JwtOptions"/> when the host starts, so a weak or incomplete configuration stops the
/// application instead of silently weakening authentication (fail closed, OWASP A10).
/// </summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        if (
            options.AccessTokenLifetime <= TimeSpan.Zero
            || options.AccessTokenLifetime > JwtOptions.MaxAccessTokenLifetime
        )
        {
            failures.Add("Jwt:AccessTokenLifetime must be greater than zero and at most 15 minutes.");
        }

        if (
            options.RefreshTokenLifetime <= TimeSpan.Zero
            || options.RefreshTokenLifetime > JwtOptions.MaxRefreshTokenLifetime
        )
        {
            failures.Add("Jwt:RefreshTokenLifetime must be greater than zero and at most 7 days.");
        }

        if (
            options.RefreshFamilyLifetime < options.RefreshTokenLifetime
            || options.RefreshFamilyLifetime > JwtOptions.MaxRefreshFamilyLifetime
        )
        {
            failures.Add("Jwt:RefreshFamilyLifetime must be at least the refresh-token lifetime and at most 30 days.");
        }

        ValidateKeys(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateKeys(JwtOptions options, List<string> failures)
    {
        if (options.Keys.Count == 0)
        {
            return; // Development generates an ephemeral key; every other environment fails in SigningKeyRing.
        }

        if (options.Keys.Any(key => string.IsNullOrWhiteSpace(key.Id)))
        {
            failures.Add("Every Jwt:Keys entry needs an Id.");
        }

        if (options.Keys.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != options.Keys.Count)
        {
            failures.Add("Jwt:Keys identifiers must be unique.");
        }

        var active = options.Keys.FirstOrDefault(key =>
            string.Equals(key.Id, options.ActiveKeyId, StringComparison.Ordinal)
        );
        if (active is null)
        {
            failures.Add("Jwt:ActiveKeyId must name one of the configured keys.");
        }
        else if (string.IsNullOrWhiteSpace(active.PrivateKeyPem) && string.IsNullOrWhiteSpace(active.PrivateKeyPemFile))
        {
            failures.Add("The active signing key needs a private key (PrivateKeyPem or PrivateKeyPemFile).");
        }
    }
}
