using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Portfolio.IntegrationTests.Caching;
using Portfolio.IntegrationTests.Database;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// Hosts the real application pipeline in-process, on a PostgreSQL database of its own cloned from the migrated
/// template of <see cref="PostgresFixture"/> and dropped on disposal (ai/TESTS.md §9.3). Valkey and RabbitMQ are
/// not wired yet; they will join through Testcontainers when their infrastructure exists.
/// Secrets are generated for each factory (a fresh ES384 key, a random token-hash key, random passwords), so
/// nothing sensitive is committed and no two runs share credentials.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string KeyId = "test-key-1";
    public const string Issuer = "https://api.portfolio.example";
    public const string Audience = "pf-ecommerce-api";

    private readonly string _environment;
    private readonly Action<IWebHostBuilder>? _configure;
    private readonly bool _withSecrets;
    private readonly int _authPermitLimit;
    private readonly FakeTimeProvider? _clock;
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    private readonly TestDatabase _database;
    private readonly string _connectionString;
    private readonly bool _ownsDatabase;

    public ApiFactory(
        string environment,
        Action<IWebHostBuilder>? configure = null,
        bool withSecrets = true,
        int authPermitLimit = 10_000,
        FakeTimeProvider? clock = null,
        TestDatabase? database = null,
        string? connectionString = null,
        string? valkeyConnectionString = null
    )
    {
        // By default the host shares the run's Valkey under a key prefix of its own; a test may point it elsewhere
        // (an unreachable address simulates an outage).
        ValkeyConnectionString = valkeyConnectionString ?? ValkeyFixture.Current.ConnectionString;
        ValkeyKeyPrefix = ValkeyFixture.NewPrefix();
        // By default the host runs on a database of its own; a test may bring one (and the credentials to reach it).
        _ownsDatabase = database is null;
        _database = database ?? PostgresFixture.Current.CreateDatabase();
        _connectionString = connectionString ?? _database.ConnectionString;
        _environment = environment;
        _configure = configure;
        _withSecrets = withSecrets;
        _authPermitLimit = authPermitLimit;
        _clock = clock;

        // One account per access level, each with its own random password.
        foreach (var level in TestAccounts.AllLevels)
        {
            Accounts[level] = new TestAccount($"{level}.user@example.com", RandomPassword(), level);
        }
    }

    /// <summary>The bootstrap accounts, one per access level.</summary>
    public Dictionary<string, TestAccount> Accounts { get; } = new(StringComparer.Ordinal);

    /// <summary>The Valkey connection string this host uses.</summary>
    public string ValkeyConnectionString { get; }

    /// <summary>The key prefix of this host: every key it writes starts with it, so a test can find them.</summary>
    public string ValkeyKeyPrefix { get; }

    /// <summary>The database this host runs on, for tests that look at what the API persisted.</summary>
    public TestDatabase Database => _database;

    /// <summary>The private key that signs tokens, for crafting tokens with deliberate defects.</summary>
    public ECDsa SigningKey => _signingKey;

    /// <summary>Generates a policy-compliant random password (32 random characters).</summary>
    public static string RandomPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(BuildSettings()));
        builder.ConfigureServices(services =>
        {
            if (_clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            }
        });
        _configure?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        // Stop the host first so nothing is using the database when it is dropped.
        base.Dispose(disposing);

        if (disposing)
        {
            _signingKey.Dispose();
            if (_ownsDatabase)
            {
                _database.Dispose();
            }
        }
    }

    private Dictionary<string, string?> BuildSettings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RateLimiting:Auth:PermitLimit"] = _authPermitLimit.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["ConnectionStrings:Postgres"] = _connectionString,
            ["ConnectionStrings:Valkey"] = ValkeyConnectionString,
            ["Valkey:KeyPrefix"] = ValkeyKeyPrefix,
        };

        if (!_withSecrets)
        {
            return settings;
        }

        settings["Jwt:ActiveKeyId"] = KeyId;
        settings["Jwt:Keys:0:Id"] = KeyId;
        settings["Jwt:Keys:0:PrivateKeyPem"] = _signingKey.ExportPkcs8PrivateKeyPem();
        settings["Identity:TokenHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        settings["Pagination:CursorKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var index = 0;
        foreach (var account in Accounts.Values)
        {
            settings[$"Identity:Bootstrap:Accounts:{index}:Email"] = account.Email;
            settings[$"Identity:Bootstrap:Accounts:{index}:Password"] = account.Password;
            settings[$"Identity:Bootstrap:Accounts:{index}:AccessLevel"] = account.Level;
            index++;
        }

        return settings;
    }
}
