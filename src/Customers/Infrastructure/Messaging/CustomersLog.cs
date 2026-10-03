namespace Portfolio.Customers.Infrastructure;

/// <summary>Log messages of the Customers consumers. Field names only: never a name, e-mail, or phone (ai/SECURITY.md §11.2).</summary>
internal static partial class CustomersLog
{
    [LoggerMessage(
        EventId = 1500,
        Level = LogLevel.Warning,
        Message = "A customer profile was created without {IgnoredCount} optional field(s) that broke a rule: {IgnoredFields}."
    )]
    public static partial void OptionalFieldsIgnored(ILogger logger, int ignoredCount, string ignoredFields);
}
