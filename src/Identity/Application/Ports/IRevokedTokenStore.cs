namespace Portfolio.Identity.Application;

/// <summary>
/// Blocklist of access-token identifiers (<c>jti</c>) revoked before their expiry (ai/SECURITY.md §7.6).
/// Entries are only needed until the token would have expired anyway.
/// </summary>
internal interface IRevokedTokenStore
{
    /// <summary>Blocks a token until <paramref name="expiresAt"/>.</summary>
    /// <param name="tokenId">The <c>jti</c> claim.</param>
    /// <param name="expiresAt">UTC instant after which the entry is unnecessary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAsync(string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Whether a token identifier is blocked.</summary>
    /// <param name="tokenId">The <c>jti</c> claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default);
}
