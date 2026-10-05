using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Application;
using Portfolio.Shipping.Domain;
using Portfolio.Shipping.Infrastructure;
using Portfolio.UnitTests.Shipping.Application;
using Portfolio.UnitTests.Shipping.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Shipping.Infrastructure;

/// <summary>
/// Shipping's consumer of the Ordering's events: it reads each event through its own message type, applies it, ignores
/// the kinds it does not use, and dead-letters what can never be handled (BR-SHP-001, BR-SHP-004, BR-SHP-005).
/// </summary>
public sealed class OrderEventsConsumerTests
{
    private static readonly Guid OrderId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57");
    private static readonly Guid CustomerId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58");

    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderReferenceRepository _references = new();
    private readonly FakeShipmentRepository _shipments = new();
    private readonly FakeInbox _inbox = new();
    private readonly OrderEventsConsumer _consumer = new();
    private readonly ServiceProvider _services;

    public OrderEventsConsumerTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<IOrderReferenceRepository>(_references)
            .AddSingleton<IShipmentRepository>(_shipments)
            .AddSingleton<IUnitOfWork>(new FakeUnitOfWork(_inbox))
            .AddSingleton<IInbox>(_inbox)
            .AddSingleton<TimeProvider>(_clock)
            .AddScoped<SyncOrderHandler>()
            .BuildServiceProvider();
    }

    private Task<bool> ConsumeAsync(string routingKey, string json, Guid? messageId = null) =>
        _consumer.ConsumeAsync(
            new ReceivedMessage(
                messageId ?? Guid.CreateVersion7(),
                routingKey,
                CorrelationId: null,
                Redelivered: false,
                Attempt: 1,
                Encoding.UTF8.GetBytes(json)
            ),
            _services,
            TestContext.Current.CancellationToken
        );

    private static string Event(string extra = "") =>
        $$"""
            {"eventId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e01","aggregateId":"{{OrderId}}","aggregateVersion":2,
             "occurredAt":"2026-10-02T12:00:00+00:00","customerId":"{{CustomerId}}","number":"PF-2026-000001"{{extra}}}
            """;

    [Fact]
    public void Should_bind_one_queue_of_its_own_to_every_ordering_event()
    {
        _consumer.Name.ShouldBe("shipping.sync-orders");
        _consumer.BindingKey.ShouldBe("ordering.*");
    }

    [Fact]
    public async Task Should_remember_the_order_from_an_order_placed_event_ignoring_the_rest_of_the_payload()
    {
        var handled = await ConsumeAsync(
            "ordering.order-placed",
            Event(""","total":{"amount":"379.80","currency":"BRL"},"items":[{"sku":"CAF-600-PRT"}]""")
        );

        handled.ShouldBeTrue();
        var reference = _references.References.ShouldHaveSingleItem();
        (reference.Id, reference.CustomerId, reference.Number).ShouldBe((OrderId, CustomerId, "PF-2026-000001"));
        _shipments.Shipments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_prepare_the_shipment_from_an_order_paid_event()
    {
        await ConsumeAsync("ordering.order-paid", Event());

        var shipment = _shipments.Shipments.ShouldHaveSingleItem();
        (shipment.OrderId, shipment.CustomerId, shipment.Status).ShouldBe(
            (OrderId, CustomerId, ShipmentStatus.Preparing)
        );
    }

    [Fact]
    public async Task Should_cancel_the_shipment_from_an_order_cancelled_event()
    {
        await ConsumeAsync("ordering.order-paid", Event());

        await ConsumeAsync("ordering.order-cancelled", Event(""","reasonCode":"changedMind","wasPaid":true"""));

        _shipments.Shipments.Single().Status.ShouldBe(ShipmentStatus.Cancelled);
    }

    [Theory]
    [InlineData("ordering.order-shipped")]
    [InlineData("ordering.order-delivered")]
    [InlineData("ordering.something-new")]
    public async Task Should_acknowledge_and_ignore_an_ordering_event_it_does_not_use(string routingKey)
    {
        var handled = await ConsumeAsync(routingKey, Event());

        handled.ShouldBeTrue();
        _references.References.ShouldBeEmpty();
        _shipments.Shipments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_dead_letter_a_cancellation_that_finds_the_carrier_already_holding_the_shipment()
    {
        _shipments.Seed(
            ShipmentBuilder.New().ForOrder(OrderId, CustomerId).WithStatus(ShipmentStatus.InTransit).Build(_clock)
        );

        var failure = await Should.ThrowAsync<PoisonMessageException>(() =>
            ConsumeAsync("ordering.order-cancelled", Event(""","reasonCode":"changedMind","wasPaid":true"""))
        );

        failure.Message.ShouldContain("cancelled after the carrier");
    }

    [Fact]
    public async Task Should_dead_letter_an_event_without_the_data_shipping_needs()
    {
        await Should.ThrowAsync<PoisonMessageException>(() =>
            ConsumeAsync("ordering.order-placed", """{"aggregateId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57"}""")
        );
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Should_dead_letter_a_body_that_is_not_an_event(string body)
    {
        await Should.ThrowAsync<PoisonMessageException>(() => ConsumeAsync("ordering.order-paid", body));
    }

    [Fact]
    public async Task Should_report_a_redelivery_as_not_handled_now()
    {
        var messageId = Guid.CreateVersion7();
        await ConsumeAsync("ordering.order-paid", Event(), messageId);

        var redelivery = await ConsumeAsync("ordering.order-paid", Event(), messageId);

        redelivery.ShouldBeFalse();
        _shipments.Shipments.Count.ShouldBe(1);
    }
}
