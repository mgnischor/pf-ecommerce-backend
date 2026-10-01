using System.Globalization;
using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Identity.Support;

/// <summary>Deterministic, instant stand-in for Argon2id: the "hash" is the password with a marker.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public const string Marker = "fake-hash:";
    public const string OutdatedMarker = "fake-old:";

    public int BurnCalls { get; private set; }

    public int HashCalls { get; private set; }

    public Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        HashCalls++;
        return Task.FromResult(Marker + password);
    }

    public Task<bool> VerifyAsync(
        string password,
        string passwordHash,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            string.Equals(passwordHash, Marker + password, StringComparison.Ordinal)
                || string.Equals(passwordHash, OutdatedMarker + password, StringComparison.Ordinal)
        );

    public Task BurnAsync(string password, CancellationToken cancellationToken = default)
    {
        BurnCalls++;
        return Task.CompletedTask;
    }

    public bool NeedsRehash(string passwordHash) => passwordHash.StartsWith(OutdatedMarker, StringComparison.Ordinal);
}

/// <summary>Sequential refresh tokens ("rt-1", "rt-2"...) whose hash is the value reversed.</summary>
internal sealed class FakeRefreshTokenCodec : IRefreshTokenCodec
{
    private int _counter;

    public GeneratedRefreshToken Generate()
    {
        var plain = $"rt-{Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture)}";
        return new GeneratedRefreshToken(plain, Hash(plain));
    }

    public string Hash(string plain) => string.Concat(plain.Reverse());
}

/// <summary>Issues a readable fake token that encodes the account's level and token version.</summary>
internal sealed class FakeAccessTokenIssuer(TimeProvider timeProvider) : IAccessTokenIssuer
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public IssuedAccessToken Issue(User user) =>
        new(
            $"access:{user.Id}:{user.AccessLevel}:{user.TokenVersion}",
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow() + Lifetime
        );
}

/// <summary>Screen that flags exactly the passwords it was told about.</summary>
internal sealed class FakeBreachedPasswordScreen(params string[] breached) : IBreachedPasswordScreen
{
    public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(breached.Contains(password, StringComparer.Ordinal));
}

/// <summary>Records every security event so tests can assert what was audited.</summary>
internal sealed class RecordingAudit : ISecurityAudit
{
    public List<string> Events { get; } = [];

    public void SignInSucceeded(Guid userId) => Events.Add("sign_in_succeeded");

    public void SignInFailed(Guid? userId, bool lockedOut) =>
        Events.Add(lockedOut ? "sign_in_failed_locked" : "sign_in_failed");

    public void AccountLocked(Guid userId) => Events.Add("account_locked");

    public void RefreshTokenReuseDetected(Guid userId, Guid familyId) => Events.Add("refresh_token_reuse");

    public void TokenRevoked(Guid? userId) => Events.Add("token_revoked");

    public void AccountCreated(Guid userId, AccessLevel level, Guid? actorId) => Events.Add($"account_created:{level}");

    public void AccessLevelChanged(Guid actorId, Guid targetId, AccessLevel from, AccessLevel to) =>
        Events.Add($"access_level_changed:{from}->{to}");

    public void AccountDeactivated(Guid actorId, Guid targetId) => Events.Add("account_deactivated");

    public void PrivilegeDenied(Guid actorId, string reasonCode) => Events.Add($"privilege_denied:{reasonCode}");
}
