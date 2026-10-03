namespace Portfolio.Customers.Infrastructure;

/// <summary>
/// Customers' view of Identity's <c>CustomerRegistered</c> event: only the fields it needs. Others in the payload are
/// ignored, and the optional ones may be absent or <c>null</c>. The message carries personal data, so it is never logged.
/// </summary>
/// <param name="AggregateId">Identifier of the registered account.</param>
/// <param name="Email">E-mail of the account.</param>
/// <param name="FullName">Name given at registration, if any.</param>
/// <param name="Phone">Phone given at registration, if any.</param>
/// <param name="Locale">Locale given at registration, if any.</param>
/// <param name="TimeZone">Time zone given at registration, if any.</param>
internal sealed record CustomerRegisteredMessage(
    Guid AggregateId,
    string? Email,
    string? FullName,
    string? Phone,
    string? Locale,
    string? TimeZone
);
