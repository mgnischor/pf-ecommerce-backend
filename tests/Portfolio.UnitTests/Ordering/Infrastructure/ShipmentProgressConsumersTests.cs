using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Ordering.Application;
using Portfolio.Ordering.Domain;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.UnitTests.Ordering.Application;
using Portfolio.UnitTests.Ordering.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Infrastructure;

/// <summary>
/// Ordering's consumers of the Shipping's events: each reads the event through its own message type and moves the
/// order forward, and each has a queue and an inbox of its own (BR-ORD-003).
/// </summary>
[Trait("Rule", "BR-ORD-003")]
public sealed class ShipmentProgressConsumersTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeInbox _inbox = new();
    private readonly ServiceProvider _services;

    public ShipmentProgressConsumersTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<IOrderRepository>(_orders)
            .AddSingleton<IUnitOfWork>(new FakeUnitOfWork(_inbox))
            .AddSingleton<IInbox>(_inbox)
            .AddSingleton<TimeProvider>(_clock)
            .AddScoped<AdvanceOrderHandler>()
            .BuildServiceProvider();
    }

    private Order Seeded(OrderStatus status)
    {
        var order = OrderBuilder.New().WithStatus(status).Build(_clock);
        _orders.Seed(order);
        return order;
    }

    private Task<bool> ConsumeAsync(ShipmentProgressConsumer consumer, string body, Guid? messageId = null) =>
        consumer.ConsumeAsync(
            new ReceivedMessage(
                messageId ?? Guid.CreateVersion7(),
                consumer.BindingKey,
                CorrelationId: null,
                Redelivered: false,
                Attempt: 1,
                Encoding.UTF8.GetBytes(body)
            ),
            _services,
            TestContext.Current.CancellationToken
        );

    private static string Event(Guid orderId, string extra = "") =>
        $$"""{"eventId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e01","aggregateId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e60","aggregateVersion":2,"occurredAt":"2026-10-02T12:00:00+00:00","orderId":"{{orderId}}"{{extra}}}""";

    [Fact]
    public void Should_have_a_queue_and_a_binding_of_their_own_each()
    {
        var dispatched = new ShipmentDispatchedConsumer();
        var delivered = new ShipmentDeliveredConsumer();

        (dispatched.Name, dispatched.BindingKey).ShouldBe(
            ("ordering.mark-shipped-on-shipment-dispatched", "shipping.shipment-dispatched")
        );
        (delivered.Name, delivered.BindingKey).ShouldBe(
            ("ordering.mark-delivered-on-shipment-delivered", "shipping.shipment-delivered")
        );
    }

    [Fact]
    public async Task Should_move_a_paid_order_to_shipped_when_the_shipment_is_dispatched()
    {
        var order = Seeded(OrderStatus.Paid);

        var handled = await ConsumeAsync(
            new ShipmentDispatchedConsumer(),
            Event(order.Id, ",\"carrier\":\"Correios\",\"trackingCode\":null,\"estimatedDeliveryDate\":\"2026-10-09\"")
        );

        handled.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Shipped);
    }

    [Fact]
    public async Task Should_move_a_shipped_order_to_delivered_when_the_shipment_is_delivered()
    {
        var order = Seeded(OrderStatus.Shipped);

        await ConsumeAsync(new ShipmentDeliveredConsumer(), Event(order.Id));

        order.Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Should_keep_the_inbox_of_each_consumer_apart_for_the_same_message()
    {
        var order = Seeded(OrderStatus.Shipped);
        var messageId = Guid.CreateVersion7();

        var first = await ConsumeAsync(new ShipmentDeliveredConsumer(), Event(order.Id), messageId);
        var otherQueue = await ConsumeAsync(new ShipmentDispatchedConsumer(), Event(order.Id), messageId);
        var redelivery = await ConsumeAsync(new ShipmentDeliveredConsumer(), Event(order.Id), messageId);

        first.ShouldBeTrue();
        otherQueue.ShouldBeTrue();
        redelivery.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_dead_letter_an_event_for_an_order_ordering_does_not_know()
    {
        await Should.ThrowAsync<PoisonMessageException>(() =>
            ConsumeAsync(new ShipmentDeliveredConsumer(), Event(Guid.CreateVersion7()))
        );
    }

    [Fact]
    public async Task Should_dead_letter_a_dispatch_for_an_order_that_was_cancelled()
    {
        var order = Seeded(OrderStatus.Cancelled);

        await Should.ThrowAsync<PoisonMessageException>(() =>
            ConsumeAsync(new ShipmentDispatchedConsumer(), Event(order.Id))
        );

        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Should_dead_letter_a_body_that_is_not_an_event(string body)
    {
        await Should.ThrowAsync<PoisonMessageException>(() => ConsumeAsync(new ShipmentDeliveredConsumer(), body));
    }
}
