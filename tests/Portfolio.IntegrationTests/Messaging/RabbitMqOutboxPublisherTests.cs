using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The RabbitMQ adapter of the outbox against a real broker (ai/TESTS.md §4.1): what a consumer receives, the trace
/// context it carries, the topology the adapter declares, and how it behaves when the broker is not there.
/// </summary>
public sealed class RabbitMqOutboxPublisherTests : IAsyncLifetime
{
    private static readonly ActivitySource Source = new("Ecommerce.Messaging");

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
    private readonly List<IAsyncDisposable> _disposables = [];
    private IConnection _consumerConnection = default!;
    private IChannel _consumer = default!;

    private RabbitMqOptions Options =>
        new()
        {
            Exchange = $"test-events-{_suffix}",
            DeadLetterExchange = $"test-events-{_suffix}.dead",
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
    }

    private RabbitMqConnection Connection(string connectionString)
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
        _disposables.Add(connection);
        return connection;
    }

    private RabbitMqOutboxPublisher PublisherFor(string connectionString)
    {
        var publisher = new RabbitMqOutboxPublisher(
            Connection(connectionString),
            Microsoft.Extensions.Options.Options.Create(Options)
        );
        _disposables.Add(publisher);
        return publisher;
    }

    private RabbitMqOutboxPublisher Publisher() => PublisherFor(RabbitMqFixture.Current.ConnectionString);

    /// <summary>A queue bound to every event, as a consumer would declare it. The exchange exists once a publisher connected.</summary>
    private async Task<string> SubscribeAsync(string bindingKey = "#")
    {
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
            bindingKey,
            cancellationToken: TestContext.Current.CancellationToken
        );
        return queue.QueueName;
    }

    private static OutboxMessage NewMessage(
        string correlationId = "corr-1",
        string? causationId = null,
        string? traceParent = null
    )
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, clock);
        return OutboxMessage.From(item.DomainEvents.Single(), correlationId, causationId, traceParent);
    }

    private async Task<BasicGetResult> GetAsync(string queue)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var result = await _consumer.BasicGetAsync(queue, autoAck: true, TestContext.Current.CancellationToken);
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("No message arrived on the queue.");
    }

    private static string Header(IReadOnlyBasicProperties properties, string name) =>
        Encoding.UTF8.GetString((byte[])properties.Headers![name]!);

    [Fact]
    public async Task Should_deliver_the_event_with_the_identity_a_consumer_deduplicates_on()
    {
        var publisher = Publisher();
        var message = NewMessage(causationId: "cause-9");
        await publisher.PublishAsync(message, TestContext.Current.CancellationToken); // connects, declares the exchange
        var queue = await SubscribeAsync();

        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);
        var delivery = await GetAsync(queue);

        delivery.RoutingKey.ShouldBe("inventory.inventory-item-opened");
        delivery.Exchange.ShouldBe(Options.Exchange);
        delivery.BasicProperties.MessageId.ShouldBe(message.Id.ToString("D"));
        delivery.BasicProperties.CorrelationId.ShouldBe("corr-1");
        delivery.BasicProperties.ContentType.ShouldBe("application/json");
        delivery.BasicProperties.DeliveryMode.ShouldBe(DeliveryModes.Persistent);
        delivery.BasicProperties.Type.ShouldBe("inventory.inventory-item-opened");
        Header(delivery.BasicProperties, "aggregate-id").ShouldBe(message.AggregateId.ToString("D"));
        Header(delivery.BasicProperties, "causation-id").ShouldBe("cause-9");
        delivery.BasicProperties.Headers!["aggregate-version"].ShouldBe(message.AggregateVersion);
        Encoding.UTF8.GetString(delivery.Body.Span).ShouldBe(message.Payload);
    }

    [Fact]
    public async Task Should_route_by_context_and_event_name_so_a_consumer_can_bind_to_one_context()
    {
        var publisher = Publisher();
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var inventory = await SubscribeAsync("inventory.*");
        var catalog = await SubscribeAsync("catalog.*");

        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);

        (await GetAsync(inventory)).RoutingKey.ShouldStartWith("inventory.");
        (await _consumer.BasicGetAsync(catalog, autoAck: true, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_carry_the_trace_context_of_the_producer_span_in_the_amqp_headers()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Ecommerce.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var publisher = Publisher();
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var queue = await SubscribeAsync();

        using var producer = Source.StartActivity("publish", ActivityKind.Producer);
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var delivery = await GetAsync(queue);

        producer.ShouldNotBeNull();
        Header(delivery.BasicProperties, "traceparent").ShouldBe(producer.Id);
        producer.GetTagItem("messaging.system").ShouldBe("rabbitmq");
        producer.GetTagItem("messaging.destination.name").ShouldBe(Options.Exchange);
        producer.GetTagItem("messaging.rabbitmq.destination.routing_key").ShouldBe("inventory.inventory-item-opened");
    }

    [Fact]
    public async Task Should_send_no_trace_headers_when_nothing_is_being_traced()
    {
        var publisher = Publisher();
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var queue = await SubscribeAsync();

        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var delivery = await GetAsync(queue);

        delivery.BasicProperties.Headers!.ContainsKey("traceparent").ShouldBeFalse();
    }

    [Fact]
    public async Task Should_not_fail_for_an_event_nobody_subscribed_to()
    {
        var publisher = Publisher();

        await Should.NotThrowAsync(() => publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Should_declare_the_durable_events_and_dead_letter_exchanges_and_tolerate_declaring_them_again()
    {
        await PublisherFor(RabbitMqFixture.Current.ConnectionString)
            .PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        await PublisherFor(RabbitMqFixture.Current.ConnectionString)
            .PublishAsync(NewMessage(), TestContext.Current.CancellationToken);

        // A passive declare fails (and closes the channel) when the exchange does not exist.
        await _consumer.ExchangeDeclarePassiveAsync(Options.Exchange, TestContext.Current.CancellationToken);
        await _consumer.ExchangeDeclarePassiveAsync(Options.DeadLetterExchange, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_publish_many_events_from_concurrent_callers_without_losing_or_duplicating_any()
    {
        var publisher = Publisher();
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
        var queue = await SubscribeAsync();
        var messages = Enumerable.Range(0, 40).Select(_ => NewMessage()).ToList();

        await Task.WhenAll(
            messages.Select(message => publisher.PublishAsync(message, TestContext.Current.CancellationToken))
        );

        var received = new List<string>();
        for (var index = 0; index < messages.Count; index++)
        {
            received.Add((await GetAsync(queue)).BasicProperties.MessageId!);
        }

        received
            .Order(StringComparer.Ordinal)
            .ShouldBe(messages.Select(message => message.Id.ToString("D")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Should_throw_when_the_broker_is_unreachable_so_the_event_stays_in_the_outbox()
    {
        var publisher = PublisherFor(RabbitMqBroker.Unreachable);

        await Should.ThrowAsync<BrokerUnreachableException>(() =>
            publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Should_report_degraded_not_unhealthy_when_the_broker_is_unreachable_and_leak_nothing()
    {
        var check = new RabbitMqHealthCheck(Connection(RabbitMqBroker.Unreachable));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["error"].ShouldBe(nameof(BrokerUnreachableException));
        (result.Description ?? string.Empty).ShouldNotContain(RabbitMqBroker.Password);
        result
            .Data.Values.OfType<string>()
            .ShouldNotContain(value => value.Contains(RabbitMqBroker.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_report_healthy_when_the_broker_answers()
    {
        var check = new RabbitMqHealthCheck(Connection(RabbitMqFixture.Current.ConnectionString));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Should_publish_again_after_the_broker_restarts()
    {
        await using var broker = await RabbitMqBroker.StartAsync();
        var publisher = PublisherFor(broker.ConnectionString);
        await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);

        await broker.StopBrokerAsync();
        await Should.ThrowAsync<Exception>(() =>
            publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken)
        );

        await broker.StartBrokerAsync();
        var published = false;
        for (var attempt = 0; attempt < 30 && !published; attempt++)
        {
            try
            {
                await publisher.PublishAsync(NewMessage(), TestContext.Current.CancellationToken);
                published = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await Task.Delay(1000, TestContext.Current.CancellationToken);
            }
        }

        published.ShouldBeTrue();
    }
}
