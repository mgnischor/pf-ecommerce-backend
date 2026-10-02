using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// Trace propagation through the outbox (ai/OBSERVABILITY.md §6.4, §17): the row stores the context of the request that
/// raised the event, and the relay's PRODUCER span continues that trace instead of starting one from its polling loop.
/// </summary>
public sealed class OutboxTracingTests : DatabaseTestBase
{
    private static readonly ActivitySource Requests = new("Test.Requests");

    private sealed class Publisher(Func<OutboxMessage, bool>? fails = null) : IOutboxPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            fails?.Invoke(message) == true ? throw new TimeoutException("broker down") : Task.CompletedTask;
    }

    private static ActivityListener Listen(List<Activity> stopped)
    {
        var listener = new ActivityListener
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
        return listener;
    }

    private (OutboxRelay<InventoryDbContext> Relay, IMeterFactory Meters) Relay(IOutboxPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddScoped(_ => TestContexts.Inventory(DataSource, Clock));
        var provider = services.BuildServiceProvider();
        var meters = provider.GetRequiredService<IMeterFactory>();

        return (
            new OutboxRelay<InventoryDbContext>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                publisher,
                Clock,
                Options.Create(new OutboxRelayOptions()),
                meters,
                NullLogger<OutboxRelay<InventoryDbContext>>.Instance
            ),
            meters
        );
    }

    private async Task<Activity?> SaveItemInsideARequestAsync(string sku = "CAF-600-PRT")
    {
        var request = Requests.StartActivity("simulated request", ActivityKind.Server);
        try
        {
            var context = Inventory();
            context.InventoryItems.Add(InventoryItem.Open(InventorySku.Create(sku).Value, Clock));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return request;
        }
        finally
        {
            request?.Stop(); // The request is over by the time the relay runs; its identifiers stay readable.
        }
    }

    [Fact]
    public async Task Should_store_the_trace_context_of_the_request_with_the_event()
    {
        var stopped = new List<Activity>();
        using var listener = Listen(stopped);

        var request = await SaveItemInsideARequestAsync();

        var stored = (
            await Database.StringsAsync("SELECT trace_parent FROM inventory.outbox_messages")
        ).ShouldHaveSingleItem();
        stored.ShouldBe(request.ShouldNotBeNull().Id);
        stored.ShouldMatch("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$");
        (await Database.ScalarAsync("SELECT correlation_id FROM inventory.outbox_messages")).ShouldBe(
            request.TraceId.ToString()
        );
    }

    [Fact]
    public async Task Should_store_no_trace_context_when_nothing_is_being_traced()
    {
        var context = Inventory();
        context.InventoryItems.Add(InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT trace_parent FROM inventory.outbox_messages")).ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task Should_continue_the_requests_trace_in_the_relays_producer_span()
    {
        var stopped = new List<Activity>();
        using var listener = Listen(stopped);
        var request = await SaveItemInsideARequestAsync();
        var (relay, _) = Relay(new Publisher());

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        Activity producer;
        lock (stopped)
        {
            producer = stopped.Single(span => span.Source.Name == "Ecommerce.Messaging");
        }

        producer.Kind.ShouldBe(ActivityKind.Producer);
        producer.TraceId.ShouldBe(request!.TraceId);
        producer.ParentSpanId.ShouldBe(request.SpanId);
        producer.DisplayName.ShouldBe("publish InventoryItemOpened");
        producer.GetTagItem("messaging.operation.type").ShouldBe("publish");
        producer.GetTagItem("app.bounded_context").ShouldBe("inventory");
        producer.Status.ShouldNotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Should_start_a_new_trace_when_the_event_has_no_stored_context_or_a_malformed_one()
    {
        var stopped = new List<Activity>();
        using var listener = Listen(stopped);
        var context = Inventory();
        context.InventoryItems.Add(InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Database.ExecuteAsync("UPDATE inventory.outbox_messages SET trace_parent = 'not-a-traceparent'");
        var (relay, _) = Relay(new Publisher());

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        (
            await Database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages WHERE processed_at IS NOT NULL")
        ).ShouldBe(1L);
        Activity producer;
        lock (stopped)
        {
            producer = stopped.Single(span => span.Source.Name == "Ecommerce.Messaging");
        }

        producer.ParentSpanId.ToString().ShouldBe(default(ActivitySpanId).ToString());
    }

    [Fact]
    public async Task Should_mark_a_failed_publication_as_an_error_with_only_the_exception_type()
    {
        var stopped = new List<Activity>();
        using var listener = Listen(stopped);
        await SaveItemInsideARequestAsync();
        var (relay, _) = Relay(new Publisher(_ => true));

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        Activity producer;
        lock (stopped)
        {
            producer = stopped.Single(span => span.Source.Name == "Ecommerce.Messaging");
        }

        producer.Status.ShouldBe(ActivityStatusCode.Error);
        producer.GetTagItem("error.type").ShouldBe("TimeoutException");
        producer
            .TagObjects.Select(tag => tag.Value?.ToString())
            .ShouldAllBe(value => value == null || !value.Contains("broker down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_record_the_publication_duration_by_outcome()
    {
        await SaveItemInsideARequestAsync("CAF-600-PRT");
        await SaveItemInsideARequestAsync("FIL-100-PAP");
        var calls = 0;
        var (relay, meters) = Relay(new Publisher(_ => Interlocked.Increment(ref calls) == 1));
        using var duration = new MetricCollector<double>(meters, "Ecommerce.Outbox", "app.outbox.publish.duration");

        await relay.RelayBatchAsync(TestContext.Current.CancellationToken);

        var outcomes = duration
            .GetMeasurementSnapshot()
            .Select(point => point.Tags["app.outcome"]?.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        outcomes.ShouldBe(["failure", "success"]);
        duration
            .GetMeasurementSnapshot()
            .ShouldAllBe(point => point.Tags["app.bounded_context"]!.ToString() == "inventory" && point.Value >= 0);
    }
}
