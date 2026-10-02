namespace Portfolio.IntegrationTests.Caching;

/// <summary>
/// One Valkey for the whole test run, like <see cref="Database.PostgresFixture"/> for PostgreSQL. Tests do not share keys:
/// each host and each cache under test gets its own key prefix.
/// </summary>
public sealed class ValkeyFixture : IAsyncLifetime
{
    private ValkeyContainer _server = default!;

    /// <summary>The fixture of the running test assembly.</summary>
    internal static ValkeyFixture Current { get; private set; } = default!;

    /// <summary>The connection string of the shared server.</summary>
    internal string ConnectionString => _server.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _server = await ValkeyContainer.StartAsync();
        Current = this;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    /// <summary>A key prefix no other test uses.</summary>
    internal static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..12];
}
