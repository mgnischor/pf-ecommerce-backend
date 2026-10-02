using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Rejects settings that would break the key scheme or the timeouts, and a missing connection string.</summary>
internal sealed partial class ValkeyOptionsValidator(IConfiguration configuration) : IValidateOptions<ValkeyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ValkeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(ValkeyOptions.ConnectionStringName)))
        {
            // Names the setting, never a value.
            errors.Add("The connection string 'Valkey' is not configured (set ConnectionStrings__Valkey).");
        }

        if (!KeyPrefix().IsMatch(options.KeyPrefix))
        {
            errors.Add(
                "Valkey:KeyPrefix must be 1 to 64 lowercase letters, digits, ':', '_' or '-', starting with a letter or digit."
            );
        }

        if (options.ConnectTimeoutMilliseconds is < 50 or > 10_000)
        {
            errors.Add("Valkey:ConnectTimeoutMilliseconds must be between 50 and 10000.");
        }

        if (options.OperationTimeoutMilliseconds is < 10 or > 5_000)
        {
            errors.Add("Valkey:OperationTimeoutMilliseconds must be between 10 and 5000.");
        }

        if (options.MaximumPayloadBytes is < 1_024 or > 1_048_576)
        {
            errors.Add("Valkey:MaximumPayloadBytes must be between 1024 and 1048576.");
        }

        if (!Enum.IsDefined(options.RevocationCheckFailureMode))
        {
            errors.Add("Valkey:RevocationCheckFailureMode must be Deny or Allow.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9:_-]{0,63}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyPrefix();
}
