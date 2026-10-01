using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Inventory.Domain;

[Trait("Rule", "BR-INV-004")]
public sealed class AvailableStockTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 10, 0)]
    public void Should_compute_available_as_on_hand_minus_reserved(int onHand, int reserved, int available)
    {
        var item = InventoryItems.With(_clock, onHand, reserved);

        item.Available.ShouldBe(available);
    }
}

[Trait("Rule", "BR-INV-005")]
public sealed class InventoryItemReservationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_reserve_available_units_and_raise_a_reserved_event()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 2);

        var result = item.Reserve(3, _clock);

        result.IsSuccess.ShouldBeTrue();
        item.Reserved.ShouldBe(5);
        item.OnHand.ShouldBe(10);
        item.Available.ShouldBe(5);
        var domainEvent = item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StockReserved>();
        domainEvent.Quantity.ShouldBe(3);
        domainEvent.Reserved.ShouldBe(5);
        domainEvent.Available.ShouldBe(5);
        domainEvent.AggregateVersion.ShouldBe(item.Version);
    }

    [Fact]
    public void Should_reserve_every_available_unit_but_not_one_more()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 2);

        item.Reserve(8, _clock).IsSuccess.ShouldBeTrue();
        var result = item.Reserve(1, _clock);

        var error = result.ShouldFail();
        error.Code.ShouldBe("STOCK_INSUFFICIENT_AVAILABLE");
        error.Type.ShouldBe(ErrorType.Conflict);
        error.RuleId.ShouldBe("BR-INV-005");
        error.ShouldHaveParameters()["requested"].ShouldBe(1);
        error.ShouldHaveParameters()["available"].ShouldBe(0);
        item.Reserved.ShouldBe(10);
    }

    [Fact]
    public void Should_not_change_anything_when_the_request_exceeds_availability()
    {
        var item = InventoryItems.With(_clock, onHand: 3);
        var versionBefore = item.Version;

        item.Reserve(4, _clock).ShouldFail().Code.ShouldBe("STOCK_INSUFFICIENT_AVAILABLE");

        item.Reserved.ShouldBe(0);
        item.Version.ShouldBe(versionBefore);
        item.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_reject_a_quantity_that_is_not_positive(int quantity)
    {
        var item = InventoryItems.With(_clock, onHand: 3);

        var error = item.Reserve(quantity, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_QUANTITY_MUST_BE_POSITIVE");
        error.Field.ShouldBe("quantity");
        item.Reserved.ShouldBe(0);
    }
}

[Trait("Rule", "BR-INV-006")]
public sealed class InventoryItemReleaseTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_release_reserved_units_back_to_the_available_stock_and_raise_a_released_event()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 6);

        var result = item.Release(4, _clock);

        result.IsSuccess.ShouldBeTrue();
        item.Reserved.ShouldBe(2);
        item.Available.ShouldBe(8);
        var domainEvent = item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StockReleased>();
        domainEvent.Quantity.ShouldBe(4);
        domainEvent.Reserved.ShouldBe(2);
        domainEvent.Available.ShouldBe(8);
    }

    [Fact]
    public void Should_reject_releasing_more_than_is_reserved_and_change_nothing()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 2);
        var versionBefore = item.Version;

        var error = item.Release(3, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_RELEASE_EXCEEDS_RESERVED");
        error.Type.ShouldBe(ErrorType.Conflict);
        error.RuleId.ShouldBe("BR-INV-006");
        error.ShouldHaveParameters()["requested"].ShouldBe(3);
        error.ShouldHaveParameters()["reserved"].ShouldBe(2);
        item.Reserved.ShouldBe(2);
        item.Version.ShouldBe(versionBefore);
        item.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Should_reject_a_quantity_that_is_not_positive(int quantity)
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 2);

        item.Release(quantity, _clock).ShouldFail().Code.ShouldBe("STOCK_QUANTITY_MUST_BE_POSITIVE");

        item.Reserved.ShouldBe(2);
    }

    [Fact]
    public void Should_keep_reserved_between_zero_and_on_hand_through_a_mixed_sequence()
    {
        var item = InventoryItems.With(_clock, onHand: 5);

        item.Reserve(5, _clock).IsSuccess.ShouldBeTrue();
        item.Adjust(-1, "damage", InventoryItems.Actor, null, _clock).IsFailure.ShouldBeTrue();
        item.Release(2, _clock).IsSuccess.ShouldBeTrue();
        item.Adjust(-2, "damage", InventoryItems.Actor, null, _clock).IsSuccess.ShouldBeTrue();

        item.OnHand.ShouldBe(3);
        item.Reserved.ShouldBe(3);
        item.Available.ShouldBe(0);
    }
}
