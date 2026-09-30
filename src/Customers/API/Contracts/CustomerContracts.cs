namespace Portfolio.Customers.API.Contracts;

/// <summary>Profile of the authenticated customer. Contact data is masked (ai/API_CONTRACTS.md §9.1).</summary>
/// <param name="Id">Customer identifier.</param>
/// <param name="FullName">Name as entered by the customer.</param>
/// <param name="MaskedEmail">E-mail with the local part masked, for example <c>a***@example.com</c>.</param>
/// <param name="MaskedPhone">Phone with all but the last digits masked, omitted when not provided.</param>
/// <param name="Locale">Preferred locale such as <c>pt-BR</c>.</param>
/// <param name="TimeZone">IANA time zone identifier.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
public sealed record CustomerProfileResponse(
    Guid Id,
    string FullName,
    string MaskedEmail,
    string? MaskedPhone,
    string Locale,
    string TimeZone,
    int Version
);
