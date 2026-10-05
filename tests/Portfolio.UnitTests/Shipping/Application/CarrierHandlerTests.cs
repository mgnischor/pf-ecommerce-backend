using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Application;
using Portfolio.Shipping.Domain;
using Portfolio.UnitTests.Shipping.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Shipping.Application;

[Trait("Rule", "BR-SHP-002")]
public sealed class DispatchShipmentHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShipmentRepository _shipments = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly DispatchShipmentHandler _handler;

    public DispatchShipmentHandlerTests()
    {
        _handler = new DispatchShipmentHandler(_shipments, _unitOfWork, _clock);
    }

    private Shipment Seeded(ShipmentStatus status = ShipmentStatus.Preparing)
    {
        var shipment = ShipmentBuilder.New().WithStatus(status).Build(_clock);
        _shipments.Seed(shipment);
        return shipment;
    }

    private Task<Result<ShipmentView>> DispatchAsync(
        Guid shipmentId,
        string? carrier = "Jadlog",
        string? tracking = "JD123",
        DateOnly? estimate = null
    ) =>
        _handler.HandleAsync(
            new DispatchShipmentCommand(shipmentId, carrier, tracking, estimate),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_hand_the_shipment_to_the_carrier_persist_it_and_return_its_view()
    {
        var shipment = Seeded();

        var result = await DispatchAsync(shipment.Id, estimate: new DateOnly(2026, 10, 9));

        var view = result.Value;
        (view.Id, view.OrderId, view.Status, view.Carrier, view.TrackingCode).ShouldBe(
            (shipment.Id, shipment.OrderId, ShipmentStatusView.InTransit, "Jadlog", "JD123")
        );
        view.EstimatedDeliveryDate.ShouldBe(new DateOnly(2026, 10, 9));
        shipment.DomainEvents.OfType<ShipmentDispatched>().ShouldHaveSingleItem();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_answer_a_repeat_of_the_same_hand_over_without_a_second_event()
    {
        var shipment = Seeded();
        await DispatchAsync(shipment.Id);

        var repeat = await DispatchAsync(shipment.Id);

        repeat.IsSuccess.ShouldBeTrue();
        repeat.Value.Status.ShouldBe(ShipmentStatusView.InTransit);
        shipment.DomainEvents.OfType<ShipmentDispatched>().Count().ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_refuse_a_different_hand_over_of_a_shipment_the_carrier_already_holds()
    {
        var shipment = Seeded();
        await DispatchAsync(shipment.Id);

        var other = await DispatchAsync(shipment.Id, "Correios");

        other.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        shipment.Carrier.ShouldBe("Jadlog");
    }

    [Fact]
    public async Task Should_refuse_to_dispatch_a_cancelled_shipment()
    {
        var shipment = Seeded(ShipmentStatus.Cancelled);

        var result = await DispatchAsync(shipment.Id);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_report_a_missing_shipment_as_not_found()
    {
        var result = await DispatchAsync(Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("SHIPMENT_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Theory]
    [InlineData(null, "SHIPMENT_CARRIER_REQUIRED")]
    [InlineData("A", "SHIPMENT_CARRIER_LENGTH")]
    public async Task Should_reject_an_invalid_carrier_and_persist_nothing(string? carrier, string code)
    {
        var shipment = Seeded();

        var result = await DispatchAsync(shipment.Id, carrier);

        result.ShouldFail().Code.ShouldBe(code);
        shipment.Status.ShouldBe(ShipmentStatus.Preparing);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}

[Trait("Rule", "BR-SHP-002")]
public sealed class ConcludeShipmentHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShipmentRepository _shipments = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ConcludeShipmentHandler _handler;

    public ConcludeShipmentHandlerTests()
    {
        _handler = new ConcludeShipmentHandler(_shipments, _unitOfWork, _clock);
    }

    private Shipment Seeded(ShipmentStatus status = ShipmentStatus.InTransit)
    {
        var shipment = ShipmentBuilder.New().WithStatus(status).Build(_clock);
        _shipments.Seed(shipment);
        return shipment;
    }

    private Task<Result<ShipmentView>> ConcludeAsync(Guid id, ShipmentOutcome outcome, string? reason = null) =>
        _handler.HandleAsync(new ConcludeShipmentCommand(id, outcome, reason), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_deliver_a_shipment_in_transit()
    {
        var shipment = Seeded();

        var result = await ConcludeAsync(shipment.Id, ShipmentOutcome.Delivered);

        result.Value.Status.ShouldBe(ShipmentStatusView.Delivered);
        shipment.DomainEvents.OfType<ShipmentDelivered>().ShouldHaveSingleItem();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_fail_a_shipment_in_transit_with_the_reason_given()
    {
        var shipment = Seeded();

        var result = await ConcludeAsync(shipment.Id, ShipmentOutcome.Failed, "addressNotFound");

        result.Value.Status.ShouldBe(ShipmentStatusView.Failed);
        shipment.FailureReason.ShouldBe("addressNotFound");
    }

    [Fact]
    public async Task Should_answer_a_repeat_of_the_same_outcome_without_a_second_event()
    {
        var delivered = Seeded();
        await ConcludeAsync(delivered.Id, ShipmentOutcome.Delivered);
        var failed = Seeded();
        await ConcludeAsync(failed.Id, ShipmentOutcome.Failed, "lost");

        var repeatDelivery = await ConcludeAsync(delivered.Id, ShipmentOutcome.Delivered);
        var repeatFailure = await ConcludeAsync(failed.Id, ShipmentOutcome.Failed, "lost");

        repeatDelivery.IsSuccess.ShouldBeTrue();
        repeatFailure.IsSuccess.ShouldBeTrue();
        delivered.DomainEvents.OfType<ShipmentDelivered>().Count().ShouldBe(1);
        failed.DomainEvents.OfType<ShipmentFailed>().Count().ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Should_refuse_a_different_outcome_after_the_shipment_ended()
    {
        var shipment = Seeded();
        await ConcludeAsync(shipment.Id, ShipmentOutcome.Delivered);

        var other = await ConcludeAsync(shipment.Id, ShipmentOutcome.Failed, "lost");

        other.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
    }

    [Fact]
    public async Task Should_refuse_to_conclude_a_shipment_the_carrier_does_not_hold_yet()
    {
        var shipment = Seeded(ShipmentStatus.Preparing);

        var result = await ConcludeAsync(shipment.Id, ShipmentOutcome.Delivered);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_failure_without_a_valid_reason_and_persist_nothing()
    {
        var shipment = Seeded();

        var result = await ConcludeAsync(shipment.Id, ShipmentOutcome.Failed, null);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_FAILURE_REASON_INVALID");
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_report_a_missing_shipment_as_not_found()
    {
        var result = await ConcludeAsync(Guid.CreateVersion7(), ShipmentOutcome.Delivered);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_NOT_FOUND");
    }
}
