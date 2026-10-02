using Microsoft.Extensions.Options;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Blocklist of access-token identifiers (<c>jti</c>) revoked by sign-out, in Valkey (ai/SECURITY.md §7.6): one key per
/// <c>jti</c> whose TTL is exactly the time the token has left, so the set is bounded by the access-token lifetime and
/// cleans itself. It is shared by every instance, which the former in-process store was not.
/// <para>
/// <b>Failure policy.</b> A write that fails is an error to the caller (sign-out must not pretend it revoked).
/// A read that fails follows <see cref="ValkeyOptions.RevocationCheckFailureMode"/>: by default the token is
/// <b>rejected</b> (fail closed, ai/SECURITY.md §2.1 A10), which makes authenticated requests fail while Valkey is down.
/// The alternative, <see cref="RevocationFailureMode.Allow"/>, is an explicit operational choice.
/// </para>
/// </summary>
internal sealed class ValkeyRevokedTokenStore(
    ValkeyConnection connection,
    IOptions<ValkeyOptions> options,
    TimeProvider timeProvider,
    ILogger<ValkeyRevokedTokenStore> logger
) : IRevokedTokenStore
{
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);

    private string KeyFor(string tokenId) => $"{options.Value.KeyPrefix}:identity:revoked-jti:{tokenId}:v1";

    /// <inheritdoc />
    public async Task RevokeAsync(
        string tokenId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        var remaining = expiresAt - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return; // Already expired: nothing to block.
        }

        var database = (await connection.GetAsync().WaitAsync(cancellationToken)).GetDatabase();
        await database.StringSetAsync(KeyFor(tokenId), "1", remaining < MinimumTtl ? MinimumTtl : remaining);
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);

        try
        {
            var database = (await connection.GetAsync().WaitAsync(cancellationToken)).GetDatabase();
            return await database.KeyExistsAsync(KeyFor(tokenId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var deny = options.Value.RevocationCheckFailureMode == RevocationFailureMode.Deny;
            RevocationLog.CheckFailed(logger, exception.GetType().Name, deny ? "rejected" : "accepted");
            return deny;
        }
    }
}
