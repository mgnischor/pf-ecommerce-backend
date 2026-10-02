namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// One RabbitMQ for the whole test run, like <see cref="Database.PostgresFixture"/> for PostgreSQL. Tests do not share
/// exchanges: each publisher under test gets its own, so a stray message from another test is never delivered to it.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqBroker _broker = default!;

    /// <summary>The fixture of the running test assembly.</summary>
    internal static RabbitMqFixture Current { get; private set; } = default!;

    /// <summary>The connection string of the shared broker.</summary>
    internal string ConnectionString => _broker.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _broker = await RabbitMqBroker.StartAsync();
        Current = this;
    }

    public async ValueTask DisposeAsync() => await _broker.DisposeAsync();
}
