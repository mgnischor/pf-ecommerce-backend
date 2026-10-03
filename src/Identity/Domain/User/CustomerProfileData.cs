namespace Portfolio.Identity.Domain;

/// <summary>
/// What a customer typed about themselves when they registered. Identity does not keep it and does not judge it: it only
/// hands it to the Customers context in <see cref="CustomerRegistered"/>, which owns the rules for these fields
/// (BR-CUS-001 to BR-CUS-005) and tolerates any of them being unusable. Every member is optional.
/// </summary>
/// <param name="FullName">Name as typed.</param>
/// <param name="Phone">Phone as typed.</param>
/// <param name="Locale">Preferred locale as typed.</param>
/// <param name="TimeZone">Time zone as typed.</param>
internal sealed record CustomerProfileData(string? FullName, string? Phone, string? Locale, string? TimeZone)
{
    /// <summary>A registration that gave no profile data.</summary>
    public static CustomerProfileData Empty { get; } = new(null, null, null, null);

    /// <summary>Blank members become <c>null</c>, so the event never carries whitespace as data.</summary>
    public CustomerProfileData Normalized() =>
        new(NullIfBlank(FullName), NullIfBlank(Phone), NullIfBlank(Locale), NullIfBlank(TimeZone));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
