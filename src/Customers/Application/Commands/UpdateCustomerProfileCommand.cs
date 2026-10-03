using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Application;

/// <summary>
/// Partial update of the authenticated customer's profile (JSON Merge Patch). Each member is a <see cref="Change{T}"/>:
/// left out means unchanged, sent means "set to this", and a sent <c>null</c> removes an optional value.
/// </summary>
/// <param name="AccountId">The caller's account, taken from the validated token (BR-CUS-007).</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
/// <param name="FullName">New name.</param>
/// <param name="Phone">New phone in E.164 form; <c>null</c> removes it.</param>
/// <param name="Locale">New locale.</param>
/// <param name="TimeZone">New IANA time zone.</param>
internal sealed record UpdateCustomerProfileCommand(
    Guid AccountId,
    int? ExpectedVersion,
    Change<string?> FullName,
    Change<string?> Phone,
    Change<string?> Locale,
    Change<string?> TimeZone
);
