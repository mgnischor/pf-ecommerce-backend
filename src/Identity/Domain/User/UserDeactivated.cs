using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Event raised when an account is deactivated (BR-IDN-006).</summary>
internal sealed record UserDeactivated(Guid EventId, Guid AggregateId, int AggregateVersion, DateTimeOffset OccurredAt)
    : IDomainEvent
{
    /// <summary>Creates the event for a deactivation. Must be called after the state change.</summary>
    /// <param name="user">The account.</param>
    public static UserDeactivated For(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDeactivated(Guid.CreateVersion7(user.UpdatedAt), user.Id, user.Version, user.UpdatedAt);
    }
}
