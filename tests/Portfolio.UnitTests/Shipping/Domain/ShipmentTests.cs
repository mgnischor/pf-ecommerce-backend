using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Shipping.Domain;

[Trait("Rule", "BR-SHP-001")]
public sealed class ShipmentPreparationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_start_preparing_a_shipment_for_the_order_and_its_owner()
    {
        var order = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();

        var shipment = Shipment.Prepare(order, customer, _clock).Value;

        (shipment.OrderId, shipment.CustomerId, shipment.Status).ShouldBe((order, customer, ShipmentStatus.Preparing));
        (shipment.Carrier, shipment.TrackingCode, shipment.EstimatedDeliveryDate).ShouldBe((null, null, null));
        shipment.Version.ShouldBe(AggregateRoot.InitialVersion);
        shipment.CreatedAt.ShouldBe(TestClock.Start);
        shipment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_require_an_order_and_a_customer()
    {
        Shipment
            .Prepare(Guid.Empty, Guid.CreateVersion7(), _clock)
            .ShouldFail()
            .Code.ShouldBe("SHIPMENT_ORDER_REQUIRED");
        Shipment
            .Prepare(Guid.CreateVersion7(), Guid.Empty, _clock)
            .ShouldFail()
            .Code.ShouldBe("SHIPMENT_ORDER_REQUIRED");
    }
}

[Trait("Rule", "BR-SHP-003")]
public sealed class ShipmentDispatchTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private Shipment Prepared() => Shipment.Prepare(Guid.CreateVersion7(), Guid.CreateVersion7(), _clock).Value;

    [Fact]
    public void Should_hand_the_shipment_to_the_carrier_and_raise_a_dispatched_event()
    {
        var shipment = Prepared();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = shipment.Dispatch("  Correios ", " BR123-456_789 ", new DateOnly(2026, 10, 5), _clock);

        result.IsSuccess.ShouldBeTrue();
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        (shipment.Carrier, shipment.TrackingCode, shipment.EstimatedDeliveryDate).ShouldBe(
            ("Correios", "BR123-456_789", new DateOnly(2026, 10, 5))
        );
        shipment.DispatchedAt.ShouldBe(TestClock.Start.AddHours(2));
        shipment.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        var dispatched = shipment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ShipmentDispatched>();
        (dispatched.AggregateId, dispatched.OrderId, dispatched.Carrier, dispatched.TrackingCode).ShouldBe(
            (shipment.Id, shipment.OrderId, "Correios", "BR123-456_789")
        );
        dispatched.EstimatedDeliveryDate.ShouldBe(new DateOnly(2026, 10, 5));
        dispatched.AggregateVersion.ShouldBe(shipment.Version);
    }

    [Fact]
    public void Should_accept_a_dispatch_without_a_tracking_code_or_an_estimate()
    {
        var shipment = Prepared();

        shipment.Dispatch("Correios", "   ", null, _clock).IsSuccess.ShouldBeTrue();

        (shipment.TrackingCode, shipment.EstimatedDeliveryDate).ShouldBe((null, null));
    }

    [Fact]
    public void Should_accept_an_estimate_on_the_day_of_the_dispatch_and_refuse_one_before_it()
    {
        var today = DateOnly.FromDateTime(TestClock.Start.UtcDateTime);

        Prepared().Dispatch("Correios", null, today, _clock).IsSuccess.ShouldBeTrue();
        var past = Prepared();
        past.Dispatch("Correios", null, today.AddDays(-1), _clock)
            .ShouldFail()
            .Code.ShouldBe("SHIPMENT_ESTIMATED_DELIVERY_IN_PAST");
        past.Status.ShouldBe(ShipmentStatus.Preparing);
    }

    [Theory]
    [InlineData(null, "SHIPMENT_CARRIER_REQUIRED")]
    [InlineData("", "SHIPMENT_CARRIER_REQUIRED")]
    [InlineData("   ", "SHIPMENT_CARRIER_REQUIRED")]
    [InlineData("A", "SHIPMENT_CARRIER_LENGTH")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "SHIPMENT_CARRIER_LENGTH")]
    public void Should_reject_a_carrier_that_is_missing_or_outside_the_length(string? carrier, string code)
    {
        var shipment = Prepared();

        var result = shipment.Dispatch(carrier, null, null, _clock);

        result.ShouldFail().Code.ShouldBe(code);
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        shipment.Status.ShouldBe(ShipmentStatus.Preparing);
        shipment.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BR 123")]
    [InlineData("BR/123")]
    [InlineData("rastreio-ç")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Should_reject_a_tracking_code_with_characters_outside_the_allowlist_or_too_long(string code)
    {
        var result = Prepared().Dispatch("Correios", code, null, _clock);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_TRACKING_CODE_INVALID");
    }

    [Theory]
    [InlineData("InTransit")]
    [InlineData("Delivered")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [Trait("Rule", "BR-SHP-002")]
    public void Should_refuse_to_dispatch_a_shipment_that_is_not_being_prepared(string status)
    {
        var shipment = ShipmentBuilder.New().WithStatus(Enum.Parse<ShipmentStatus>(status)).Build(_clock);
        var versionBefore = shipment.Version;

        var result = shipment.Dispatch("Outra", null, null, _clock);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        shipment.Version.ShouldBe(versionBefore);
    }

    [Fact]
    public void Should_recognise_a_repeat_of_the_same_hand_over_and_not_a_different_one()
    {
        var shipment = Prepared();
        shipment.Dispatch("Correios", "BR123", new DateOnly(2026, 10, 9), _clock);

        shipment.WasDispatchedWith(" Correios ", " BR123 ", new DateOnly(2026, 10, 9)).ShouldBeTrue();
        shipment.WasDispatchedWith("Jadlog", "BR123", new DateOnly(2026, 10, 9)).ShouldBeFalse();
        shipment.WasDispatchedWith("Correios", "BR999", new DateOnly(2026, 10, 9)).ShouldBeFalse();
        shipment.WasDispatchedWith("Correios", "BR123", null).ShouldBeFalse();
        Prepared().WasDispatchedWith("Correios", null, null).ShouldBeFalse();
    }
}

[Trait("Rule", "BR-SHP-002")]
public sealed class ShipmentLifecycleTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private Shipment Shipment(ShipmentStatus status) => ShipmentBuilder.New().WithStatus(status).Build(_clock);

    [Fact]
    public void Should_deliver_a_shipment_in_transit_and_raise_a_delivered_event()
    {
        var shipment = Shipment(ShipmentStatus.InTransit);
        _clock.Advance(TimeSpan.FromDays(2));

        shipment.MarkDelivered(_clock).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.DeliveredAt.ShouldBe(TestClock.Start.AddDays(2));
        var delivered = shipment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ShipmentDelivered>();
        (delivered.AggregateId, delivered.OrderId, delivered.AggregateVersion).ShouldBe(
            (shipment.Id, shipment.OrderId, shipment.Version)
        );
    }

    [Fact]
    public void Should_fail_a_shipment_in_transit_with_a_reason_and_raise_a_failed_event()
    {
        var shipment = Shipment(ShipmentStatus.InTransit);

        shipment.MarkFailed("  addressNotFound ", _clock).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Failed);
        shipment.FailureReason.ShouldBe("addressNotFound");
        shipment.FailedAt.ShouldBe(TestClock.Start);
        var failed = shipment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ShipmentFailed>();
        failed.ReasonCode.ShouldBe("addressNotFound");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("1abc")]
    [InlineData("two words")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Should_reject_a_failure_reason_that_is_not_a_short_identifier_and_change_nothing(string? reason)
    {
        var shipment = Shipment(ShipmentStatus.InTransit);

        var result = shipment.MarkFailed(reason, _clock);

        result.ShouldFail().Code.ShouldBe("SHIPMENT_FAILURE_REASON_INVALID");
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
    }

    [Fact]
    public void Should_cancel_a_shipment_that_is_still_being_prepared_and_raise_a_cancelled_event()
    {
        var shipment = Shipment(ShipmentStatus.Preparing);

        shipment.Cancel(_clock).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Cancelled);
        shipment.CancelledAt.ShouldBe(TestClock.Start);
        shipment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ShipmentCancelled>();
    }

    [Theory]
    [InlineData("Preparing", "Delivered")]
    [InlineData("Preparing", "Failed")]
    [InlineData("InTransit", "Cancelled")]
    [InlineData("Delivered", "Delivered")]
    [InlineData("Delivered", "Failed")]
    [InlineData("Delivered", "Cancelled")]
    [InlineData("Failed", "Delivered")]
    [InlineData("Failed", "Failed")]
    [InlineData("Failed", "Cancelled")]
    [InlineData("Cancelled", "Delivered")]
    [InlineData("Cancelled", "Failed")]
    [InlineData("Cancelled", "Cancelled")]
    public void Should_refuse_a_transition_the_state_machine_does_not_have_and_change_nothing(string from, string to)
    {
        var shipment = Shipment(Enum.Parse<ShipmentStatus>(from));
        var versionBefore = shipment.Version;

        var result = to switch
        {
            "Delivered" => shipment.MarkDelivered(_clock),
            "Failed" => shipment.MarkFailed("lost", _clock),
            _ => shipment.Cancel(_clock),
        };

        var error = result.ShouldFail();
        error.Code.ShouldBe("SHIPMENT_INVALID_STATUS_TRANSITION");
        error.Type.ShouldBe(ErrorType.Conflict);
        error.ShouldHaveParameters().ShouldBe(new Dictionary<string, object> { ["from"] = from, ["to"] = to });
        shipment.Status.ShouldBe(Enum.Parse<ShipmentStatus>(from));
        shipment.Version.ShouldBe(versionBefore);
        shipment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_recognise_an_outcome_the_shipment_already_has_and_not_a_different_one()
    {
        var delivered = Shipment(ShipmentStatus.Delivered);
        var failed = Shipment(ShipmentStatus.InTransit);
        failed.MarkFailed("lost", _clock);

        delivered.HasConcludedWith(true, null).ShouldBeTrue();
        delivered.HasConcludedWith(false, "lost").ShouldBeFalse();
        failed.HasConcludedWith(false, " lost ").ShouldBeTrue();
        failed.HasConcludedWith(false, "damaged").ShouldBeFalse();
        failed.HasConcludedWith(true, null).ShouldBeFalse();
        Shipment(ShipmentStatus.InTransit).HasConcludedWith(true, null).ShouldBeFalse();
    }
}

[Trait("Rule", "BR-SHP-004")]
public sealed class OrderReferenceTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_remember_whose_an_order_is_reusing_the_order_identifier()
    {
        var order = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();

        var reference = OrderReference.Record(order, customer, "  PF-2026-000001 ", _clock).Value;

        (reference.Id, reference.CustomerId, reference.Number).ShouldBe((order, customer, "PF-2026-000001"));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58", "PF-2026-000001")]
    [InlineData("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57", "00000000-0000-0000-0000-000000000000", "PF-2026-000001")]
    [InlineData("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57", "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58", "")]
    [InlineData("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57", "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e58", null)]
    public void Should_reject_a_record_missing_the_order_the_customer_or_the_number(
        string order,
        string customer,
        string? number
    )
    {
        var result = OrderReference.Record(Guid.Parse(order), Guid.Parse(customer), number, _clock);

        result.ShouldFail().Code.ShouldBe("SHIPPING_ORDER_EVENT_MALFORMED");
    }
}
