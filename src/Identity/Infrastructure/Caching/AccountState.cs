using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// What a token check needs to know about an account, and nothing else: no e-mail, no hash, no name. This is the cached
/// value of <see cref="AccountStateCache"/>, so its shape is versioned with the cache entry (<c>v1</c>).
/// </summary>
/// <param name="Exists">Whether the account exists and is not deleted.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="TokenVersion">Version that every valid access token must carry.</param>
/// <param name="Level">Access level that every valid access token must carry.</param>
internal sealed record AccountState(bool Exists, UserStatus Status, int TokenVersion, AccessLevel Level)
{
    /// <summary>The state of an account that does not exist: no token can match it.</summary>
    public static AccountState Missing { get; } = new(false, UserStatus.Deactivated, 0, AccessLevel.Public);

    /// <summary>Whether a token carrying <paramref name="version"/> and <paramref name="level"/> is still valid for the account.</summary>
    /// <param name="version">Token version claim.</param>
    /// <param name="level">Access level claim.</param>
    public bool Accepts(int version, AccessLevel level) =>
        Exists && Status == UserStatus.Active && TokenVersion == version && Level == level;
}
