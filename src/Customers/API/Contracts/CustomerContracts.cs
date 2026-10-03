using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Portfolio.Customers.API.Contracts;

/// <summary>Profile of the authenticated customer. Contact data is masked (ai/API_CONTRACTS.md §9.1).</summary>
/// <param name="Id">Customer identifier.</param>
/// <param name="FullName">Name as entered by the customer; <c>null</c> until they give one.</param>
/// <param name="MaskedEmail">E-mail with the local part masked, for example <c>a***@example.com</c>.</param>
/// <param name="MaskedPhone">Phone with all but the last four digits masked, <c>null</c> when not provided.</param>
/// <param name="Locale">Preferred locale: <c>pt-BR</c> or <c>en</c>.</param>
/// <param name="TimeZone">IANA time zone identifier.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
internal sealed record CustomerProfileResponse(
    Guid Id,
    string? FullName,
    string MaskedEmail,
    string? MaskedPhone,
    string Locale,
    string TimeZone,
    int Version
);

/// <summary>
/// Partial update of the profile (JSON Merge Patch, RFC 7396): a member that is left out keeps its value, a member that
/// is sent replaces it, and <c>phone: null</c> removes the phone. The name, locale, and time zone can be changed but not
/// removed. Identity, e-mail, and version cannot be changed here. The rules are enforced by the use case, which answers
/// <c>422</c> with the field and the rule identifier; the attributes below only bound the size of the input.
/// </summary>
/// <remarks>
/// A plain record cannot tell a missing member from a <c>null</c> one, so the members remember that they were set:
/// <c>System.Text.Json</c> calls the <c>init</c> accessor only for members present in the body.
/// </remarks>
internal sealed class UpdateCustomerProfileRequest
{
    private readonly string? _fullName;
    private readonly string? _phone;
    private readonly string? _locale;
    private readonly string? _timeZone;

    /// <summary>New name, 2–120 characters.</summary>
    [StringLength(512)]
    public string? FullName
    {
        get => _fullName;
        init
        {
            _fullName = value;
            FullNameSet = true;
        }
    }

    /// <summary>New phone as an E.164 number such as <c>+5511987654321</c>; <c>null</c> removes it.</summary>
    [StringLength(64)]
    public string? Phone
    {
        get => _phone;
        init
        {
            _phone = value;
            PhoneSet = true;
        }
    }

    /// <summary>New preferred locale: <c>pt-BR</c> or <c>en</c>.</summary>
    [StringLength(32)]
    public string? Locale
    {
        get => _locale;
        init
        {
            _locale = value;
            LocaleSet = true;
        }
    }

    /// <summary>New IANA time zone identifier such as <c>America/Sao_Paulo</c>.</summary>
    [StringLength(128)]
    public string? TimeZone
    {
        get => _timeZone;
        init
        {
            _timeZone = value;
            TimeZoneSet = true;
        }
    }

    /// <summary>Whether <c>fullName</c> was in the body.</summary>
    [JsonIgnore]
    public bool FullNameSet { get; private set; }

    /// <summary>Whether <c>phone</c> was in the body.</summary>
    [JsonIgnore]
    public bool PhoneSet { get; private set; }

    /// <summary>Whether <c>locale</c> was in the body.</summary>
    [JsonIgnore]
    public bool LocaleSet { get; private set; }

    /// <summary>Whether <c>timeZone</c> was in the body.</summary>
    [JsonIgnore]
    public bool TimeZoneSet { get; private set; }
}
