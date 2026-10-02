using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>
/// Event raised when every access token of an account is invalidated without a change of level or status (a refresh
/// token was replayed, BR-IDN-005). Derived consumers, such as the account cache, evict what they hold for the account.
/// </summary>
internal sealed record UserTokensRevoked(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    /// <summary>Creates the event for a revocation. Must be called after the state change.</summary>
    /// <param name="user">The account.</param>
    public static UserTokensRevoked For(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserTokensRevoked(Guid.CreateVersion7(user.UpdatedAt), user.Id, user.Version, user.UpdatedAt);
    }
}
