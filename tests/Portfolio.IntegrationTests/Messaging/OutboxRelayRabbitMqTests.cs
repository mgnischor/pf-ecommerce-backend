using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.IntegrationTests.Database;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;
using RabbitMQ.Client;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The whole path of ai/DATABASE.md §3.1 against real PostgreSQL and RabbitMQ: a state change writes its event to the
/// outbox in the same transaction, the relay publishes it, and a broker that is down only delays it.
/// </summary>
public sealed class OutboxRelayRabbitMqTests : DatabaseTestBase, IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
    private readonly List<IAsyncDisposable> _disposables = [];
    private IConnection _consumerConnection = default!;
    private IChannel _consumer = default!;

    private RabbitMqOptions Options =>
        new()
        {
            Exchange = $"test-relay-{_suffix}",
            DeadLetterExchange = $"test-relay-{_suffix}.dead",
            PublishTimeout = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(2),
        };

    public async ValueTask InitializeAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqFixture.Current.ConnectionString) };
        _consumerConnection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        _consumer = await _consumerConnection.CreateChannelAsync(
            cancellationToken: TestContext.Current.CancellationToken
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _consumer.DisposeAsync();
        await _consumerConnection.DisposeAsync();
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        base.Dispose();
    }

    private OutboxRelay<InventoryDbContext> RelayTo(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionStrings:RabbitMQ"] = connectionString,
                }
            )
            .Build();
        var connection = new RabbitMqConnection(
            configuration,
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<RabbitMqConnection>.Instance
        );
        var publisher = new RabbitMqOutboxPublisher(connection, Microsoft.Extensions.Options.Options.Create(Options));
        _disposables.Add(connection);
        _disposables.Add(publisher);

        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddScoped(_ => TestContexts.Inventory(DataSource, Clock));
        var provider = services.BuildServiceProvider();

        return new OutboxRelay<InventoryDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Clock,
            Microsoft.Extensions.Options.Options.Create(new OutboxRelayOptions()),
            provider.GetRequiredService<IMeterFactory>(),
            NullLogger<OutboxRelay<InventoryDbContext>>.Instance
        );
    }

    private async Task<string> BoundQueueAsync()
    {
        await _consumer.ExchangeDeclareAsync(
            Options.Exchange,
            ExchangeType.Topic,
            durable: true,
            cancellationToken: TestContext.Current.CancellationToken
        );
        var queue = await _consumer.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: TestContext.Current.CancellationToken
        );
        await _consumer.QueueBindAsync(
            queue.QueueName,
            Options.Exchange,
            "inventory.*",
            cancellationToken: TestContext.Current.CancellationToken
        );
        return queue.QueueName;
    }

    private async Task<Guid> SaveItemAsync()
    {
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        var eventId = item.DomainEvents.Single().EventId; // saving clears the aggregate's events
        var context = Inventory();
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return eventId;
    }

    [Fact]
    public async Task Should_publish_the_event_written_with_the_state_change_and_mark_it_processed()
    {
        var queue = await BoundQueueAsync();
        var eventId = await SaveItemAsync();

        var claimed = await RelayTo(RabbitMqFixture.Current.ConnectionString)
            .RelayBatchAsync(TestContext.Current.CancellationToken);

        claimed.ShouldBe(1);
        var delivery = await _consumer.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken);
        delivery.ShouldNotBeNull().BasicProperties.MessageId.ShouldBe(eventId.ToString("D"));
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NOT NULL")
        ).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_keep_the_event_pending_while_the_broker_is_down_and_deliver_it_once_it_is_back()
    {
        var eventId = await SaveItemAsync();

        await RelayTo(RabbitMqBroker.Unreachable).RelayBatchAsync(TestContext.Current.CancellationToken);

        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NULL")
        ).ShouldBe(1L);
        var error = (
            await Database.StringsAsync("SELECT last_error FROM inventory.outbox_messages")
        ).ShouldHaveSingleItem();
        error.ShouldNotBeNullOrEmpty();
        error.ShouldNotContain(RabbitMqBroker.Password);

        var queue = await BoundQueueAsync();
        Clock.Advance(TimeSpan.FromMinutes(5)); // past the lease taken by the failed attempt
        await RelayTo(RabbitMqFixture.Current.ConnectionString).RelayBatchAsync(TestContext.Current.CancellationToken);

        var delivery = await _consumer.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken);
        delivery.ShouldNotBeNull().BasicProperties.MessageId.ShouldBe(eventId.ToString("D"));
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NULL")
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_not_publish_an_event_twice_once_it_is_processed()
    {
        var queue = await BoundQueueAsync();
        await SaveItemAsync();
        var relay = RelayTo(RabbitMqFixture.Current.ConnectionString);

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        second.ShouldBe(0);
        (await _consumer.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await _consumer.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
