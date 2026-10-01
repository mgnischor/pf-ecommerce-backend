using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Catalog.Domain;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Infrastructure;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The transactional outbox (ai/DATABASE.md §3.1, §8.2): a state change and its events commit together or not at
/// all, and the relay delivers each event at least once without two instances claiming the same row.
/// </summary>
public sealed class OutboxTests : DatabaseTestBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Should_write_the_event_in_the_same_transaction_as_the_state_change()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        var raised = product.DomainEvents.ShouldHaveSingleItem();

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = (
            await Database.StringsAsync(
                "SELECT id || '|' || type || '|' || aggregate_id || '|' || aggregate_version || '|' || attempts FROM catalog.outbox_messages"
            )
        ).ShouldHaveSingleItem();
        row.ShouldBe($"{raised.EventId}|{typeof(ProductCreated).FullName}|{product.Id}|1|0");
        (await Database.ScalarAsync("SELECT processed_at FROM catalog.outbox_messages")).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task Should_store_the_event_payload_as_queryable_jsonb()
    {
        var product = NewProduct(price: 189.90m);
        var context = Catalog();
        context.Products.Add(product);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT payload->>'sku' FROM catalog.outbox_messages")).ShouldBe("CAF-600-PRT");
        (
            await Database.ScalarAsync("SELECT (payload->'price'->>'amount')::numeric FROM catalog.outbox_messages")
        ).ShouldBe(189.90m);
        (await Database.ScalarAsync("SELECT payload->'price'->>'currency' FROM catalog.outbox_messages")).ShouldBe(
            "BRL"
        );
    }

    [Fact]
    public async Task Should_queue_nothing_when_the_state_change_does_not_commit()
    {
        var context = Catalog();
        context.Products.Add(NewProduct());
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var duplicate = Catalog();
        duplicate.Products.Add(NewProduct());
        await Should.ThrowAsync<PersistenceConflictException>(() =>
            duplicate.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        // Only the first product's event: the failed commit rolled its own outbox row back with its state.
        (await Database.ScalarAsync("SELECT count(*) FROM catalog.outbox_messages")).ShouldBe(1L);
        (await Database.ScalarAsync("SELECT count(*) FROM catalog.products")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_clear_the_events_once_committed_and_never_queue_one_twice()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        product.DomainEvents.ShouldBeEmpty();

        product.Activate(Clock);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (
            await Database.StringsAsync(
                "SELECT aggregate_version FROM catalog.outbox_messages ORDER BY aggregate_version"
            )
        ).ShouldBe(["1", "2"]);
    }

    [Fact]
    public async Task Should_queue_every_event_of_every_aggregate_saved_together_in_the_order_they_occurred()
    {
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        item.Adjust(10, "stocktake", Guid.CreateVersion7(), "key-0001-aaaa", Clock);
        item.Reserve(3, Clock);
        var context = Inventory();
        context.InventoryItems.Add(item);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (
            await Database.StringsAsync(
                "SELECT split_part(type, '.', 4) FROM inventory.outbox_messages ORDER BY aggregate_version"
            )
        ).ShouldBe(["InventoryItemOpened", "StockAdjusted", "StockReserved"]);
    }

    [Fact]
    public async Task Should_keep_each_contexts_events_in_its_own_schema()
    {
        var catalog = Catalog();
        catalog.Products.Add(NewProduct());
        await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        var inventory = Inventory();
        inventory.InventoryItems.Add(InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock));
        await inventory.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT count(*) FROM catalog.outbox_messages")).ShouldBe(1L);
        (await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages")).ShouldBe(1L);
        (await Database.ScalarAsync("SELECT count(*) FROM identity.outbox_messages")).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_deserialize_the_payload_back_into_the_event()
    {
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        var context = Inventory();
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var payload = (
            await Database.StringsAsync("SELECT payload::text FROM inventory.outbox_messages")
        ).ShouldHaveSingleItem();

        var restored = JsonSerializer.Deserialize<InventoryItemOpened>(payload, Json);
        restored.ShouldNotBeNull().AggregateId.ShouldBe(item.Id);
        restored.Sku.ShouldBe("CAF-600-PRT");
    }

    // ---- The relay ----------------------------------------------------------------------------------------------

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        private readonly List<Guid> _published = [];

        public TimeSpan Delay { get; init; }

        public Func<OutboxMessage, bool> Fails { get; init; } = _ => false;

        public IReadOnlyList<Guid> Published
        {
            get
            {
                lock (_published)
                {
                    return [.. _published];
                }
            }
        }

        public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Fails(message))
            {
                throw new TimeoutException("the broker is unreachable (secret detail that must not be stored)");
            }

            lock (_published)
            {
                _published.Add(message.Id);
            }
        }
    }

    private OutboxRelay<InventoryDbContext> Relay(
        RecordingPublisher publisher,
        int batchSize = 50,
        int maxAttempts = 10
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => TestContexts.Inventory(DataSource, Clock));
        var provider = services.BuildServiceProvider();

        return new OutboxRelay<InventoryDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Clock,
            Options.Create(
                new OutboxRelayOptions
                {
                    BatchSize = batchSize,
                    MaxAttempts = maxAttempts,
                    Lease = TimeSpan.FromSeconds(30),
                }
            ),
            NullLogger<OutboxRelay<InventoryDbContext>>.Instance
        );
    }

    private async Task SeedEventsAsync(int count)
    {
        var context = Inventory();
        for (var index = 0; index < count; index++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(10));
            context.InventoryItems.Add(InventoryItem.Open(InventorySku.Create($"SKU-{index:D4}").Value, Clock));
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_publish_pending_events_in_order_and_mark_them_processed()
    {
        await SeedEventsAsync(5);
        var expected = await Database.StringsAsync("SELECT id FROM inventory.outbox_messages ORDER BY occurred_at");
        var publisher = new RecordingPublisher();

        var relayed = await Relay(publisher).RelayBatchAsync(TestContext.Current.CancellationToken);

        relayed.ShouldBe(5);
        publisher.Published.Select(id => id.ToString()).ShouldBe(expected);
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NOT NULL")
        ).ShouldBe(5L);
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE locked_until IS NOT NULL")
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_not_publish_an_event_that_was_already_processed()
    {
        await SeedEventsAsync(3);
        var publisher = new RecordingPublisher();
        var relay = Relay(publisher);
        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        var second = await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        second.ShouldBe(0);
        publisher.Published.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_keep_a_failed_event_pending_count_the_attempt_and_store_only_the_error_type()
    {
        await SeedEventsAsync(2);
        var failing = new RecordingPublisher { Fails = _ => true };

        await Relay(failing).RelayBatchAsync(TestContext.Current.CancellationToken);

        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NULL")
        ).ShouldBe(2L);
        (
            await Database.StringsAsync("SELECT attempts || ':' || last_error FROM inventory.outbox_messages")
        ).ShouldAllBe(row => row == "1:TimeoutException");
        (
            await Database.ScalarAsync(
                "SELECT count(*) FROM inventory.outbox_messages WHERE last_error LIKE '%secret%'"
            )
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_deliver_an_event_that_failed_once_on_a_later_attempt()
    {
        await SeedEventsAsync(1);
        var flaky = 0;
        var publisher = new RecordingPublisher { Fails = _ => Interlocked.Increment(ref flaky) == 1 };
        var relay = Relay(publisher);

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);
        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        publisher.Published.Count.ShouldBe(1);
        (await Database.ScalarAsync("SELECT attempts FROM inventory.outbox_messages")).ShouldBe(2);
        (await Database.ScalarAsync("SELECT last_error FROM inventory.outbox_messages")).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task Should_stop_retrying_after_the_maximum_attempts_and_leave_the_event_for_an_operator()
    {
        await SeedEventsAsync(1);
        var relay = Relay(new RecordingPublisher { Fails = _ => true }, maxAttempts: 3);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await relay.RelayBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        }

        (await relay.RelayBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (
            await Database.ScalarAsync("SELECT processed_at IS NULL AND attempts = 3 FROM inventory.outbox_messages")
        ).ShouldBe(true);
    }

    [Fact]
    public async Task Should_not_claim_a_row_that_another_instance_has_leased_until_its_lease_expires()
    {
        await SeedEventsAsync(1);
        await Database.ExecuteAsync("UPDATE inventory.outbox_messages SET locked_until = '2026-10-01T12:05:00Z'");
        var publisher = new RecordingPublisher();
        var relay = Relay(publisher);

        (await relay.RelayBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        // The instance that held the lease died: once it expires, the row is claimable again.
        Clock.Advance(TimeSpan.FromMinutes(10));
        (await relay.RelayBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        publisher.Published.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_publish_each_event_exactly_once_when_instances_run_side_by_side()
    {
        await SeedEventsAsync(30);
        var publisher = new RecordingPublisher { Delay = TimeSpan.FromMilliseconds(150) };

        var claimed = await Task.WhenAll(
            Enumerable
                .Range(0, 3)
                .Select(_ => Relay(publisher, batchSize: 10).RelayBatchAsync(TestContext.Current.CancellationToken))
        );

        claimed.Sum().ShouldBe(30);
        publisher.Published.Count.ShouldBe(30);
        publisher.Published.Distinct().Count().ShouldBe(30);
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NOT NULL")
        ).ShouldBe(30L);
    }

    [Fact]
    public async Task Should_only_relay_the_outbox_of_its_own_context()
    {
        await SeedEventsAsync(2);
        var catalog = Catalog();
        catalog.Products.Add(NewProduct());
        await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Relay(new RecordingPublisher()).RelayBatchAsync(TestContext.Current.CancellationToken);

        (
            await Database.ScalarAsync("SELECT count(*) FROM catalog.outbox_messages WHERE processed_at IS NULL")
        ).ShouldBe(1L);
        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NULL")
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_serve_the_relay_from_the_partial_index_on_pending_rows()
    {
        await SeedEventsAsync(3);

        var index = await Database.StringsAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'inventory' AND indexname = 'ix_outbox_messages_pending'"
        );

        index.ShouldHaveSingleItem().ShouldContain("WHERE (processed_at IS NULL)");
    }
}
