using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.Identity.Infrastructure;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Support;

/// <summary>
/// Wires the Identity use cases to fast fakes and the in-memory stores, so handler tests exercise the real
/// domain and application code without Argon2id, cryptography, or a database.
/// </summary>
internal sealed class IdentityWorld
{
    public const string StrongPassword = "correct-horse-battery-staple";

    public IdentityWorld()
    {
        Users = new InMemoryUserRepository();
        RefreshTokens = new InMemoryRefreshTokenRepository();
        Revoked = new InMemoryRevokedTokenStore(Clock);
        Lifetimes = new TokenLifetimes(FakeAccessTokenIssuer.Lifetime, TimeSpan.FromDays(7), TimeSpan.FromDays(30));
        Factory = new TokenPairFactory(new FakeAccessTokenIssuer(Clock), Codec, RefreshTokens, Lifetimes, Clock);
        Policy = new PasswordPolicy(new FakeBreachedPasswordScreen("password-is-breached"));
    }

    public Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock { get; } = TestClock.Create();

    public InMemoryUserRepository Users { get; }

    public InMemoryRefreshTokenRepository RefreshTokens { get; }

    public InMemoryRevokedTokenStore Revoked { get; }

    public InMemoryUnitOfWork UnitOfWork { get; } = new();

    public FakePasswordHasher Hasher { get; } = new();

    public FakeRefreshTokenCodec Codec { get; } = new();

    public RecordingAudit Audit { get; } = new();

    public TokenLifetimes Lifetimes { get; }

    public TokenPairFactory Factory { get; }

    public PasswordPolicy Policy { get; }

    public SignInHandler SignIn => new(Users, Hasher, Factory, Audit, UnitOfWork, Lifetimes, Clock);

    public RefreshTokenHandler Refresh => new(RefreshTokens, Users, Codec, Factory, Audit, UnitOfWork, Clock);

    public RevokeTokenHandler Revoke => new(RefreshTokens, Codec, Revoked, Audit, UnitOfWork, Clock);

    public RegisterCustomerHandler Register => new(Users, Policy, Hasher, Audit, UnitOfWork, Clock);

    public CreateUserHandler CreateUser => new(Users, Policy, Hasher, Audit, UnitOfWork, Clock);

    public ChangeUserAccessLevelHandler ChangeLevel => new(Users, Audit, UnitOfWork, Clock);

    public DeactivateUserHandler Deactivate => new(Users, RefreshTokens, Audit, UnitOfWork, Clock);

    public async Task<User> AddUserAsync(
        string email = "ana.souza@example.com",
        AccessLevel level = AccessLevel.Public,
        string password = StrongPassword
    )
    {
        var user = User.Register(EmailAddress.Create(email).Value, await Hasher.HashAsync(password), level, Clock);
        await Users.AddAsync(user);
        return user;
    }
}
