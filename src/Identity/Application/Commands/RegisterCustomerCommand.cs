namespace Portfolio.Identity.Application;

/// <summary>Request to create a customer account (public access level).</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Chosen password. Never logged or echoed.</param>
/// <param name="FullName">Name the customer typed, if any. Not judged here: the Customers context owns its rules.</param>
/// <param name="Phone">Phone the customer typed, if any.</param>
/// <param name="Locale">Preferred locale the customer typed, if any.</param>
/// <param name="TimeZone">Time zone the customer typed, if any.</param>
internal sealed record RegisterCustomerCommand(
    string? Email,
    string? Password,
    string? FullName = null,
    string? Phone = null,
    string? Locale = null,
    string? TimeZone = null
);
