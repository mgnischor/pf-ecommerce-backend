using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Rejects settings that would defeat the pool, the timeouts, or the retry policy.</summary>
internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (options.MaxPoolSize is < 1 or > 500)
        {
            errors.Add("Database:MaxPoolSize must be between 1 and 500.");
        }

        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            errors.Add("Database:CommandTimeoutSeconds must be between 1 and 300.");
        }

        if (options.ConnectionTimeoutSeconds is < 1 or > 120)
        {
            errors.Add("Database:ConnectionTimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaxRetryCount is < 0 or > 10)
        {
            errors.Add("Database:MaxRetryCount must be between 0 and 10.");
        }

        if (options.MaxRetryDelaySeconds is < 1 or > 60)
        {
            errors.Add("Database:MaxRetryDelaySeconds must be between 1 and 60.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
