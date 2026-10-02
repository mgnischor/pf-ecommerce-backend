using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Rejects a missing or malformed connection string and settings that would break the topology.</summary>
internal sealed partial class RabbitMqOptionsValidator(IConfiguration configuration) : IValidateOptions<RabbitMqOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        var connectionString = configuration.GetConnectionString(RabbitMqOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Names the setting, never a value.
            errors.Add("The connection string 'RabbitMQ' is not configured (set ConnectionStrings__RabbitMQ).");
        }
        else if (
            !Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps")
        )
        {
            // The value itself is not echoed: it carries the password.
            errors.Add("The connection string 'RabbitMQ' must be an amqp:// or amqps:// URI.");
        }

        if (!ExchangeName().IsMatch(options.Exchange))
        {
            errors.Add("RabbitMq:Exchange must be 1 to 128 letters, digits, '.', '_' or '-'.");
        }

        if (!ExchangeName().IsMatch(options.DeadLetterExchange))
        {
            errors.Add("RabbitMq:DeadLetterExchange must be 1 to 128 letters, digits, '.', '_' or '-'.");
        }

        if (string.Equals(options.Exchange, options.DeadLetterExchange, StringComparison.Ordinal))
        {
            errors.Add("RabbitMq:DeadLetterExchange must differ from RabbitMq:Exchange.");
        }

        if (options.PublishTimeout < TimeSpan.FromSeconds(1) || options.PublishTimeout > TimeSpan.FromMinutes(2))
        {
            errors.Add("RabbitMq:PublishTimeout must be between 00:00:01 and 00:02:00.");
        }

        if (options.ConnectTimeout < TimeSpan.FromSeconds(1) || options.ConnectTimeout > TimeSpan.FromMinutes(1))
        {
            errors.Add("RabbitMq:ConnectTimeout must be between 00:00:01 and 00:01:00.");
        }

        if (options.PrefetchCount is < 1 or > 1_000)
        {
            errors.Add("RabbitMq:PrefetchCount must be between 1 and 1000.");
        }

        if (options.MaxDeliveries is < 1 or > 100)
        {
            errors.Add("RabbitMq:MaxDeliveries must be between 1 and 100.");
        }

        if (options.RetryBackoff < TimeSpan.Zero || options.RetryBackoff > TimeSpan.FromMinutes(1))
        {
            errors.Add("RabbitMq:RetryBackoff must be between 00:00:00 and 00:01:00.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExchangeName();
}
