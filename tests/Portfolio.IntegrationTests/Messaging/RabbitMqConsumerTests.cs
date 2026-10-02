using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Catalog.Domain;
using Portfolio.IntegrationTests.Database;
using Portfolio.Inventory.Application;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;
using RabbitMQ.Client;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The consumer host against a real broker and a real database (ai/TESTS.md §4.4): the queue topology it declares, the
/// inbox-backed idempotency of the Inventory consumer, retries, the delivery limit, poison messages, trace
/// continuation and a graceful stop.
/// </summary>
public sealed class RabbitMqConsumerTests : DatabaseTestBase, IAsyncLifetime
{
    private static readonly ActivitySource Requests = new("Test.Requests");

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<string> _queues = [OpenInventoryItemOnProductCreatedHandler.ConsumerName];
    private IConnection _connection = default!;
    private IChannel _channel = default!;
    private RabbitMqConsumerService? _service;

    private sealed class StubConsumer(string name, string bindingKey, Func<ReceivedMessage, int, Task<bool>> handle)
        : IMessageConsumer
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => name;

        public string BindingKey => bindingKey;

        public Task<bool> ConsumeAsync(
            ReceivedMessage message,
            IServiceProvider services,
            CancellationToken cancellationToken
        ) => handle(message, Interlocked.Increment(ref _calls));
    }

    private RabbitMqOptions Options =>
        new()
        {
            Exchange = $"test-consume-{_suffix}",
            DeadLetterExchange = $"test-consume-{_suffix}.dead",
            PublishTimeout = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PrefetchCount = 5,
            MaxDeliveries = 3,
            RetryBackoff = TimeSpan.FromMilliseconds(20),
        };

    public async ValueTask InitializeAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqFixture.Current.ConnectionString) };
        _connection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }

        foreach (var queue in _queues.SelectMany(name => new[] { name, $"{name}.dead" }))
        {
            await _channel.QueueDeleteAsync(queue, cancellationToken: CancellationToken.None);
        }

        await _channel.ExchangeDeleteAsync(Options.Exchange, cancellationToken: CancellationToken.None);
        await _channel.ExchangeDeleteAsync(Options.DeadLetterExchange, cancellationToken: CancellationToken.None);
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        Dispose();
    }

    private RabbitMqConnection NewConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionStrings:RabbitMQ"] = RabbitMqFixture.Current.ConnectionString,
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

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped(_ => TestContexts.Inventory(DataSource, Clock));
        services.AddScoped<IInventoryItemRepository, EfInventoryItemRepository>();
        services.AddConsumerUseCase<InventoryDbContext, OpenInventoryItemOnProductCreatedHandler>();
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return provider;
    }

    /// <summary>Starts the host with the given consumers and waits until every one of them is consuming.</summary>
    private async Task<ServiceProvider> StartAsync(params IMessageConsumer[] consumers)
    {
        var provider = Services();
        foreach (var consumer in consumers)
        {
            _queues.Add(consumer.Name);
        }

        _service = new RabbitMqConsumerService(
            NewConnection(),
            consumers,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(Options),
            TimeProvider.System,
            provider.GetRequiredService<IMeterFactory>(),
            NullLogger<RabbitMqConsumerService>.Instance
        );
        await _service.StartAsync(TestContext.Current.CancellationToken);

        foreach (var consumer in consumers)
        {
            await WaitUntilAsync(async () => await ConsumerCountAsync(consumer.Name) == 1);
        }

        return provider;
    }

    private async Task<uint> ConsumerCountAsync(string queue)
    {
        // A passive declare of a missing queue closes its channel, so each probe uses a channel of its own.
        await using var probe = await _connection.CreateChannelAsync(
            cancellationToken: TestContext.Current.CancellationToken
        );
        try
        {
            return (await probe.QueueDeclarePassiveAsync(queue, TestContext.Current.CancellationToken)).ConsumerCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition did not become true in time.");
    }

    private RabbitMqOutboxPublisher Publisher()
    {
        var publisher = new RabbitMqOutboxPublisher(
            NewConnection(),
            Microsoft.Extensions.Options.Options.Create(Options)
        );
        _disposables.Add(publisher);
        return publisher;
    }

    private OutboxMessage ProductCreatedMessage(string sku = "CAF-600-PRT")
    {
        var product = NewProduct(sku);
        return OutboxMessage.From(product.DomainEvents.OfType<ProductCreated>().Single(), "corr-1", null);
    }

    private Task<object?> ItemCountAsync() => Database.ScalarAsync("SELECT count(*) FROM inventory.inventory_items");

    private Task<object?> InboxCountAsync() => Database.ScalarAsync("SELECT count(*) FROM inventory.inbox_messages");

    private async Task PublishRawAsync(string routingKey, byte[] body, string? messageId)
    {
        var properties = new BasicProperties { MessageId = messageId, DeliveryMode = DeliveryModes.Persistent };
        await _channel.BasicPublishAsync(
            Options.Exchange,
            routingKey,
            mandatory: false,
            properties,
            body,
            TestContext.Current.CancellationToken
        );
    }

    private async Task<BasicGetResult> DeadLetterAsync(string consumerName)
    {
        BasicGetResult? result = null;
        await WaitUntilAsync(async () =>
        {
            result = await _channel.BasicGetAsync(
                $"{consumerName}.dead",
                autoAck: true,
                TestContext.Current.CancellationToken
            );
            return result is not null;
        });
        return result!;
    }

    private static async Task<long> CountAsync(IChannel channel, string queue) =>
        (await channel.QueueDeclarePassiveAsync(queue, TestContext.Current.CancellationToken)).MessageCount;

    // ---- The Inventory consumer -----------------------------------------------------------------------------------

    [Fact]
    public async Task Should_open_the_inventory_item_of_a_created_product_and_record_the_message_in_the_inbox()
    {
        await StartAsync(new ProductCreatedConsumer());
        var message = ProductCreatedMessage();

        await Publisher().PublishAsync(message, TestContext.Current.CancellationToken);

        await WaitUntilAsync(async () => Equals(await ItemCountAsync(), 1L));
        (await Database.StringsAsync("SELECT sku FROM inventory.inventory_items"))
            .ShouldHaveSingleItem()
            .ShouldBe("CAF-600-PRT");
        (await Database.StringsAsync("SELECT consumer FROM inventory.inbox_messages"))
            .ShouldHaveSingleItem()
            .ShouldBe(OpenInventoryItemOnProductCreatedHandler.ConsumerName);
        (await Database.StringsAsync("SELECT message_id::text FROM inventory.inbox_messages"))
            .ShouldHaveSingleItem()
            .ShouldBe(message.Id.ToString("D"));
    }

    [Fact]
    public async Task Should_have_no_effect_when_the_same_event_is_delivered_twice()
    {
        var provider = await StartAsync(new ProductCreatedConsumer());
        using var consumed = new MetricCollector<long>(
            provider.GetRequiredService<IMeterFactory>(),
            "Ecommerce.Messaging",
            "app.messaging.consumed"
        );
        var message = ProductCreatedMessage();
        var publisher = Publisher();

        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);
        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);

        await WaitUntilAsync(() =>
            Task.FromResult(
                consumed.GetMeasurementSnapshot().Count(m => Equals(m.Tags["app.outcome"], "duplicate")) == 1
            )
        );
        (await ItemCountAsync()).ShouldBe(1L);
        (await InboxCountAsync()).ShouldBe(1L);
        consumed.GetMeasurementSnapshot().Count(m => Equals(m.Tags["app.outcome"], "processed")).ShouldBe(1);
    }

    [Fact]
    public async Task Should_leave_an_item_opened_by_hand_alone_and_still_acknowledge_the_event()
    {
        var existing = Inventory();
        existing.InventoryItems.Add(InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock));
        await existing.SaveChangesAsync(TestContext.Current.CancellationToken);
        await StartAsync(new ProductCreatedConsumer());

        await Publisher().PublishAsync(ProductCreatedMessage(), TestContext.Current.CancellationToken);

        await WaitUntilAsync(async () => Equals(await InboxCountAsync(), 1L));
        (await ItemCountAsync()).ShouldBe(1L);
        (await CountAsync(_channel, OpenInventoryItemOnProductCreatedHandler.ConsumerName)).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_dead_letter_an_event_with_an_sku_inventory_rejects_without_retrying_it()
    {
        await StartAsync(new ProductCreatedConsumer());

        await PublishRawAsync(
            "catalog.product-created",
            Encoding.UTF8.GetBytes("""{"sku":"x"}"""),
            Guid.NewGuid().ToString("D")
        );

        var dead = await DeadLetterAsync(OpenInventoryItemOnProductCreatedHandler.ConsumerName);
        dead.RoutingKey.ShouldBe(OpenInventoryItemOnProductCreatedHandler.ConsumerName);
        (await ItemCountAsync()).ShouldBe(0L);
        (await InboxCountAsync()).ShouldBe(0L);
    }

    // ---- Poison, retry and delivery limit -----------------------------------------------------------------------

    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    public async Task Should_dead_letter_a_body_that_can_never_be_read(string body)
    {
        await StartAsync(new ProductCreatedConsumer());

        await PublishRawAsync("catalog.product-created", Encoding.UTF8.GetBytes(body), Guid.NewGuid().ToString("D"));

        var dead = await DeadLetterAsync(OpenInventoryItemOnProductCreatedHandler.ConsumerName);
        Encoding.UTF8.GetString(dead.Body.Span).ShouldBe(body);
    }

    [Fact]
    public async Task Should_dead_letter_a_message_without_a_usable_message_id()
    {
        await StartAsync(new ProductCreatedConsumer());

        await PublishRawAsync("catalog.product-created", Encoding.UTF8.GetBytes("""{"sku":"CAF-600-PRT"}"""), "nope");

        await DeadLetterAsync(OpenInventoryItemOnProductCreatedHandler.ConsumerName);
        (await ItemCountAsync()).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_requeue_a_failing_message_and_succeed_once_the_dependency_recovers()
    {
        var consumer = new StubConsumer(
            $"test.retry-{_suffix}",
            $"test.retry-{_suffix}",
            (_, call) => call < 3 ? throw new TimeoutException("a dependency is down") : Task.FromResult(true)
        );
        await StartAsync(consumer);

        await PublishRawAsync(consumer.BindingKey, Encoding.UTF8.GetBytes("{}"), Guid.NewGuid().ToString("D"));

        await WaitUntilAsync(async () => consumer.Calls == 3 && await CountAsync(_channel, consumer.Name) == 0);
        (await CountAsync(_channel, $"{consumer.Name}.dead")).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_dead_letter_a_message_that_keeps_failing_once_the_delivery_limit_is_reached()
    {
        var attempts = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var consumer = new StubConsumer(
            $"test.limit-{_suffix}",
            $"test.limit-{_suffix}",
            (message, _) =>
            {
                attempts.Enqueue(message.Attempt);
                throw new TimeoutException("never recovers");
            }
        );
        await StartAsync(consumer);

        await PublishRawAsync(consumer.BindingKey, Encoding.UTF8.GetBytes("{}"), Guid.NewGuid().ToString("D"));

        await DeadLetterAsync(consumer.Name);
        attempts.ShouldBe([1, 2, 3]);
        consumer.Calls.ShouldBe(Options.MaxDeliveries);
    }

    [Fact]
    public async Task Should_deliver_only_the_messages_matching_the_consumers_binding_key()
    {
        var consumer = new StubConsumer(
            $"test.bind-{_suffix}",
            $"test.bind-{_suffix}",
            (_, _) => Task.FromResult(true)
        );
        await StartAsync(consumer);

        await PublishRawAsync("test.other", Encoding.UTF8.GetBytes("{}"), Guid.NewGuid().ToString("D"));
        await PublishRawAsync(consumer.BindingKey, Encoding.UTF8.GetBytes("{}"), Guid.NewGuid().ToString("D"));

        await WaitUntilAsync(() => Task.FromResult(consumer.Calls == 1));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        consumer.Calls.ShouldBe(1);
    }

    // ---- Observability and shutdown ---------------------------------------------------------------------------

    [Fact]
    public async Task Should_continue_the_publishers_trace_in_the_consumers_span()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Test.Requests" or "Ecommerce.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var consumer = new StubConsumer(
            $"test.trace-{_suffix}",
            $"test.trace-{_suffix}",
            (_, _) => Task.FromResult(true)
        );
        await StartAsync(consumer);

        using (var request = Requests.StartActivity("publisher", ActivityKind.Producer))
        {
            request.ShouldNotBeNull();
            var properties = new BasicProperties
            {
                MessageId = Guid.NewGuid().ToString("D"),
                CorrelationId = "corr-7",
                Headers = new Dictionary<string, object?>(StringComparer.Ordinal) { ["traceparent"] = request.Id },
            };
            await _channel.BasicPublishAsync(
                Options.Exchange,
                consumer.BindingKey,
                mandatory: false,
                properties,
                Encoding.UTF8.GetBytes("{}"),
                TestContext.Current.CancellationToken
            );

            Activity? span = null;
            await WaitUntilAsync(() =>
            {
                lock (stopped)
                {
                    span = stopped.FirstOrDefault(a => a.Kind == ActivityKind.Consumer);
                }

                return Task.FromResult(span is not null);
            });

            span!.TraceId.ShouldBe(request.TraceId);
            span.ParentSpanId.ShouldBe(request.SpanId);
            span.DisplayName.ShouldBe($"process {consumer.Name}");
            span.GetTagItem("messaging.operation.type").ShouldBe("process");
            span.GetTagItem("messaging.system").ShouldBe("rabbitmq");
            span.GetTagItem("app.correlation_id").ShouldBe("corr-7");
            span.GetTagItem("app.messaging.duplicate").ShouldBe(false);
            span.Status.ShouldNotBe(ActivityStatusCode.Error);
        }
    }

    [Fact]
    public async Task Should_let_an_in_flight_handler_finish_and_acknowledge_before_the_host_stops()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new StubConsumer(
            $"test.drain-{_suffix}",
            $"test.drain-{_suffix}",
            async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return true;
            }
        );
        await StartAsync(consumer);
        await PublishRawAsync(consumer.BindingKey, Encoding.UTF8.GetBytes("{}"), Guid.NewGuid().ToString("D"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var stopping = _service!.StopAsync(CancellationToken.None);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        stopping.IsCompleted.ShouldBeFalse();

        release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        (await CountAsync(_channel, consumer.Name)).ShouldBe(0L); // acknowledged, not left unacknowledged for redelivery
        consumer.Calls.ShouldBe(1);
    }
}
