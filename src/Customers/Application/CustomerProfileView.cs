namespace Portfolio.Customers.Application;

/// <summary>
/// Read model of a customer profile: what the API may show, without exposing the aggregate. Contact data is already
/// masked here (BR-CUS-009), so no layer above can reveal it by accident.
/// </summary>
/// <param name="Id">Customer (account) identifier.</param>
/// <param name="FullName">Name as entered, or <c>null</c> until the customer gives one.</param>
/// <param name="MaskedEmail">E-mail with the local part masked.</param>
/// <param name="MaskedPhone">Phone with all but the last digits masked, or <c>null</c> when none.</param>
/// <param name="Locale">Preferred locale.</param>
/// <param name="TimeZone">IANA time zone identifier.</param>
/// <param name="Version">Aggregate version, the source of the <c>ETag</c>.</param>
internal sealed record CustomerProfileView(
    Guid Id,
    string? FullName,
    string MaskedEmail,
    string? MaskedPhone,
    string Locale,
    string TimeZone,
    int Version
);
