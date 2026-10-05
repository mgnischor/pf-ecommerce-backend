using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Application;
using Portfolio.Shipping.Domain;
using Portfolio.UnitTests.Shipping.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Shipping.Application;

public sealed class SyncOrderHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderReferenceRepository _references = new();
    private readonly FakeShipmentRepository _shipments = new();
    private readonly FakeInbox _inbox = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly SyncOrderHandler _handler;
    private readonly Guid _order = Guid.CreateVersion7();
    private readonly Guid _customer = Guid.CreateVersion7();

    public SyncOrderHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_inbox);
        _handler = new SyncOrderHandler(_references, _shipments, _unitOfWork, _inbox, _clock);
    }

    private Task<Result<bool>> SyncAsync(
        OrderEventKind kind,
        Guid? messageId = null,
        Guid? order = null,
        Guid? customer = null,
        string? number = "PF-2026-000001"
    ) =>
        _handler.HandleAsync(
            new SyncOrderCommand(
                messageId ?? Guid.CreateVersion7(),
                kind,
                order ?? _order,
                customer ?? _customer,
                number
            ),
            TestContext.Current.CancellationToken
        );

    [Fact]
    [Trait("Rule", "BR-SHP-004")]
    public async Task Should_remember_whose_a_placed_order_is_without_creating_a_shipment()
    {
        var result = await SyncAsync(OrderEventKind.Placed);

        result.Value.ShouldBeTrue();
        var reference = _references.References.ShouldHaveSingleItem();
        (reference.Id, reference.CustomerId, reference.Number).ShouldBe((_order, _customer, "PF-2026-000001"));
        _shipments.Shipments.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-004")]
    public async Task Should_not_record_the_same_order_twice()
    {
        await SyncAsync(OrderEventKind.Placed);

        var second = await SyncAsync(OrderEventKind.Placed);

        second.Value.ShouldBeTrue();
        _references.References.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-001")]
    public async Task Should_prepare_a_shipment_when_the_order_is_paid_and_know_the_order_even_if_placed_is_behind()
    {
        var result = await SyncAsync(OrderEventKind.Paid);

        result.Value.ShouldBeTrue();
        var shipment = _shipments.Shipments.ShouldHaveSingleItem();
        (shipment.OrderId, shipment.CustomerId, shipment.Status).ShouldBe(
            (_order, _customer, ShipmentStatus.Preparing)
        );
        _references.References.ShouldHaveSingleItem().Id.ShouldBe(_order);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-001")]
    public async Task Should_not_prepare_a_second_shipment_when_the_paid_event_arrives_twice_under_two_messages()
    {
        await SyncAsync(OrderEventKind.Paid);

        var second = await SyncAsync(OrderEventKind.Paid);

        second.Value.ShouldBeTrue();
        _shipments.Shipments.Count.ShouldBe(1);
        _references.References.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_do_nothing_when_the_same_message_is_delivered_again()
    {
        var messageId = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Paid, messageId);

        var redelivery = await SyncAsync(OrderEventKind.Paid, messageId);

        redelivery.Value.ShouldBeFalse();
        _shipments.Shipments.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-005")]
    public async Task Should_cancel_the_shipment_that_is_still_being_prepared_when_the_order_is_cancelled()
    {
        await SyncAsync(OrderEventKind.Paid);

        var result = await SyncAsync(OrderEventKind.Cancelled);

        result.Value.ShouldBeTrue();
        var shipment = _shipments.Shipments.ShouldHaveSingleItem();
        shipment.Status.ShouldBe(ShipmentStatus.Cancelled);
        shipment.DomainEvents.OfType<ShipmentCancelled>().ShouldHaveSingleItem();
        _shipments.UpdateCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-005")]
    public async Task Should_accept_a_cancellation_of_an_order_that_never_had_a_shipment()
    {
        var result = await SyncAsync(OrderEventKind.Cancelled);

        result.Value.ShouldBeTrue();
        _shipments.Shipments.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-SHP-005")]
    public async Task Should_accept_a_second_cancellation_of_a_shipment_that_is_already_cancelled_without_a_second_event()
    {
        await SyncAsync(OrderEventKind.Paid);
        await SyncAsync(OrderEventKind.Cancelled);

        var again = await SyncAsync(OrderEventKind.Cancelled);

        again.Value.ShouldBeTrue();
        _shipments.Shipments.Single().DomainEvents.OfType<ShipmentCancelled>().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData("InTransit")]
    [InlineData("Delivered")]
    [InlineData("Failed")]
    [Trait("Rule", "BR-SHP-005")]
    public async Task Should_report_a_cancellation_that_finds_the_carrier_holding_the_shipment_as_a_conflict(
        string status
    )
    {
        var shipment = ShipmentBuilder
            .New()
            .ForOrder(_order, _customer)
            .WithStatus(Enum.Parse<ShipmentStatus>(status))
            .Build(_clock);
        _shipments.Seed(shipment);

        var result = await SyncAsync(OrderEventKind.Cancelled);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        shipment.Status.ShouldBe(Enum.Parse<ShipmentStatus>(status));
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_redeliver_a_conflicting_cancellation_as_handled_again_because_nothing_was_committed()
    {
        var shipment = ShipmentBuilder
            .New()
            .ForOrder(_order, _customer)
            .WithStatus(ShipmentStatus.InTransit)
            .Build(_clock);
        _shipments.Seed(shipment);
        var messageId = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Cancelled, messageId);
        _inbox.EndDelivery();

        var retry = await SyncAsync(OrderEventKind.Cancelled, messageId);

        retry.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "Paid")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Placed")]
    public async Task Should_reject_an_event_without_an_order_as_malformed_before_touching_the_inbox(
        string order,
        string kind
    )
    {
        var result = await SyncAsync(Enum.Parse<OrderEventKind>(kind), order: Guid.Parse(order));

        result.ShouldFail().Code.ShouldBe("SHIPPING_ORDER_EVENT_MALFORMED");
        _inbox.Begun.ShouldBe(0);
    }

    [Theory]
    [InlineData("Paid", "00000000-0000-0000-0000-000000000000", "PF-2026-000001")]
    [InlineData("Placed", "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58", null)]
    [InlineData("Placed", "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58", " ")]
    public async Task Should_reject_an_event_missing_the_customer_or_the_number_and_persist_nothing(
        string kind,
        string customer,
        string? number
    )
    {
        var result = await SyncAsync(Enum.Parse<OrderEventKind>(kind), customer: Guid.Parse(customer), number: number);

        result.IsFailure.ShouldBeTrue();
        _shipments.Shipments.ShouldBeEmpty();
        _references.References.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
