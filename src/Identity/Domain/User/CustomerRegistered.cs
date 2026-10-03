using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>
/// Event raised when a customer registers themselves, next to <see cref="UserRegistered"/>. It exists apart from that
/// event on purpose: <see cref="UserRegistered"/> carries no personal data and every consumer of accounts may bind to
/// it, while this one carries the e-mail and what the customer typed, so only the consumer that needs them (the Customers
/// context, to create the profile) binds to it. Staff accounts created by an administrator never raise it.
/// </summary>
/// <remarks>
/// The payload is personal data (ai/SECURITY.md §11.2, ai/DATABASE.md §9): it travels through the outbox and the broker
/// to one queue, and is never logged or put in telemetry.
/// </remarks>
internal sealed record CustomerRegistered(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Email,
    string? FullName,
    string? Phone,
    string? Locale,
    string? TimeZone
) : IDomainEvent
{
    /// <summary>Creates the event for a newly registered customer. Must be called after the state change.</summary>
    /// <param name="user">The created account.</param>
    /// <param name="profile">What the customer typed at registration.</param>
    public static CustomerRegistered For(User user, CustomerProfileData profile)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(profile);

        var data = profile.Normalized();
        return new CustomerRegistered(
            Guid.CreateVersion7(user.CreatedAt),
            user.Id,
            user.Version,
            user.CreatedAt,
            user.Email.Value,
            data.FullName,
            data.Phone,
            data.Locale,
            data.TimeZone
        );
    }
}
