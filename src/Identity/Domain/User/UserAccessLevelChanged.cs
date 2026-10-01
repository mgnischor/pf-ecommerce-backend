using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Event raised when an account's access level changes (BR-IDN-004); audited as a privilege change.</summary>
internal sealed record UserAccessLevelChanged(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    AccessLevel From,
    AccessLevel To
) : IDomainEvent
{
    /// <summary>Creates the event for a level change. Must be called after the state change.</summary>
    /// <param name="user">The account.</param>
    /// <param name="from">Level before the change.</param>
    public static UserAccessLevelChanged For(User user, AccessLevel from)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserAccessLevelChanged(
            Guid.CreateVersion7(user.UpdatedAt),
            user.Id,
            user.Version,
            user.UpdatedAt,
            from,
            user.AccessLevel
        );
    }
}
