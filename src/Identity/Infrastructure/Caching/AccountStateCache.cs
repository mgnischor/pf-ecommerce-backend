using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Cache-aside read of the state of an account for token validation (ai/OBSERVABILITY.md gap D2): every authenticated request
/// used to load the account from PostgreSQL. The entry is keyed by the account id (never shared between users),
/// has a short TTL, keeps no in-process copy (so an eviction by any instance is seen by all), and is evicted by
/// <see cref="AccountCacheInvalidator"/> the moment the account's level, status or token version changes.
/// </summary>
internal sealed class AccountStateCache(IResilientCache cache, IUserRepository users)
{
    /// <summary>
    /// The cached entry. TTL 30 s: the longest a missed eviction can leave a stale answer (an eviction is lost only while
    /// Valkey is unreachable, and an unreachable cache serves nothing stale). Invalidated by the events
    /// <c>UserAccessLevelChanged</c>, <c>UserDeactivated</c> and <c>UserTokensRevoked</c>.
    /// </summary>
    public static readonly CacheEntry Entry = new(
        Name: "identity.account",
        Context: "identity",
        Entity: "account",
        Version: 1,
        Ttl: TimeSpan.FromSeconds(30)
    );

    /// <summary>Reads the state of an account, from the cache or, on a miss or an outage, from the database.</summary>
    /// <param name="userId">Account identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AccountState> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            Entry,
            userId.ToString("N"),
            async token =>
            {
                var user = await users.GetByIdAsync(userId, token);
                return user is null
                    ? AccountState.Missing
                    : new AccountState(true, user.Status, user.TokenVersion, user.AccessLevel);
            },
            cancellationToken
        );
}
