namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Cache-aside over Valkey that can never fail a request (ai/DATABASE.md §4.2, "Fallback Behavior"): when the cache is
/// slow or unreachable the value is read from its source of truth, and the failure is logged, counted and swallowed
/// here, at the cache boundary. A failure of the <em>factory</em> (the source of truth) is not a cache failure and
/// always reaches the caller, exactly once.
/// </summary>
internal interface IResilientCache
{
    /// <summary>
    /// Returns the cached value, or computes it with <paramref name="factory"/> (single-flight per key: concurrent callers
    /// share one computation), stores it for <see cref="CacheEntry.Ttl"/>, and returns it.
    /// </summary>
    /// <typeparam name="T">Cached type; it must serialize with <c>System.Text.Json</c>.</typeparam>
    /// <param name="entry">Definition of the cached data.</param>
    /// <param name="id">Opaque identifier of the entity.</param>
    /// <param name="factory">Reads the value from the source of truth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<T> GetOrCreateAsync<T>(
        CacheEntry entry,
        string id,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken
    );

    /// <summary>Evicts one value. A failure is swallowed: the TTL bounds how long a stale value can live.</summary>
    /// <param name="entry">Definition of the cached data.</param>
    /// <param name="id">Opaque identifier of the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(CacheEntry entry, string id, CancellationToken cancellationToken);
}
