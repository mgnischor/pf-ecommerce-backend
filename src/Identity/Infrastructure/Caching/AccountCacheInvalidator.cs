using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Evicts the cached state of an account when an event that changes what a token may do has committed
/// (ai/DATABASE.md §4.2: TTL plus event-driven invalidation). It runs after the commit, so a rolled-back change never
/// evicts, and before the next request can be answered with the old state in the common case. The 30-second TTL is the
/// safety net for the one case it cannot cover: Valkey being unreachable at the instant of the commit.
/// </summary>
internal sealed class AccountCacheInvalidator(IResilientCache cache) : IDomainEventSubscriber
{
    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent is UserAccessLevelChanged or UserDeactivated or UserTokensRevoked
            ? cache.RemoveAsync(AccountStateCache.Entry, domainEvent.AggregateId.ToString("N"), cancellationToken)
            : Task.CompletedTask;
    }
}
