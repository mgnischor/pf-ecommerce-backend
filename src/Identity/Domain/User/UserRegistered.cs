using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Event raised when an account is created. Carries no personal data (ai/SECURITY.md §11.2).</summary>
internal sealed record UserRegistered(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    AccessLevel Level
) : IDomainEvent
{
    /// <summary>Creates the event for a newly registered account. Must be called after the state change.</summary>
    /// <param name="user">The created account.</param>
    public static UserRegistered For(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserRegistered(
            Guid.CreateVersion7(user.CreatedAt),
            user.Id,
            user.Version,
            user.CreatedAt,
            user.AccessLevel
        );
    }
}
