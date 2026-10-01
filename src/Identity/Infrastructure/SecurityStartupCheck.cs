using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Forces the secrets-dependent services to be built while the host starts. They are otherwise created on
/// first use, which would let a production instance without a signing key or token-hash key start and only
/// fail on the first authenticated request. Failing at startup is the fail-closed behavior the standard asks
/// for (ai/SECURITY.md §5; OWASP A10).
/// </summary>
/// <param name="keys">The signing key ring (throws when no usable key exists).</param>
/// <param name="codec">The refresh-token codec (throws when its key is missing or weak).</param>
internal sealed class SecurityStartupCheck(SigningKeyRing keys, IRefreshTokenCodec codec) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Touching the instances proves they were constructed; construction is what validates the secrets.
        _ = keys.Signing;
        _ = codec;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
