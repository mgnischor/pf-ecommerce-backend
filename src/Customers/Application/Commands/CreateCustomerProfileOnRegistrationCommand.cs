namespace Portfolio.Customers.Application;

/// <summary>
/// Reaction to Identity's <c>CustomerRegistered</c> integration event: create the profile of the new customer.
/// Customers' own view of the event; it never references Identity's types.
/// </summary>
/// <param name="MessageId">Identifier of the event, which the inbox deduplicates on.</param>
/// <param name="AccountId">Identifier of the registered account; it becomes the identifier of the profile.</param>
/// <param name="Email">E-mail of the account.</param>
/// <param name="FullName">Name the customer gave at registration, if any.</param>
/// <param name="Phone">Phone the customer gave at registration, if any.</param>
/// <param name="Locale">Locale the customer gave at registration, if any.</param>
/// <param name="TimeZone">Time zone the customer gave at registration, if any.</param>
internal sealed record CreateCustomerProfileOnRegistrationCommand(
    Guid MessageId,
    Guid AccountId,
    string? Email,
    string? FullName,
    string? Phone,
    string? Locale,
    string? TimeZone
);
