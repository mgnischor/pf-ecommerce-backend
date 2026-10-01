using System.Collections.Concurrent;
using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Temporary in-process <c>jti</c> blocklist until the Valkey-backed store exists. Entries expire with the
/// token they block, so the set stays bounded by the access-token lifetime.
/// </summary>
internal sealed class InMemoryRevokedTokenStore(TimeProvider timeProvider) : IRevokedTokenStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task RevokeAsync(string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        Purge();
        _revoked[tokenId] = expiresAt;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        return Task.FromResult(
            _revoked.TryGetValue(tokenId, out var expiresAt) && expiresAt > timeProvider.GetUtcNow()
        );
    }

    private void Purge()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var entry in _revoked.Where(entry => entry.Value <= now))
        {
            _revoked.TryRemove(entry.Key, out _);
        }
    }
}
