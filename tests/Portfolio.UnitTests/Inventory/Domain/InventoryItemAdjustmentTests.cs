using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Inventory.Domain;

[Trait("Rule", "BR-INV-003")]
public sealed class InventoryItemOpeningTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_open_an_item_with_no_stock_and_raise_an_opened_event()
    {
        var item = InventoryItem.Open(InventoryItems.SkuOf("caf-600-prt"), _clock);

        item.Sku.Value.ShouldBe("CAF-600-PRT");
        item.OnHand.ShouldBe(0);
        item.Reserved.ShouldBe(0);
        item.Available.ShouldBe(0);
        item.Version.ShouldBe(AggregateRoot.InitialVersion);
        item.Movements.ShouldBeEmpty();
        var domainEvent = item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<InventoryItemOpened>();
        domainEvent.Sku.ShouldBe("CAF-600-PRT");
        domainEvent.AggregateId.ShouldBe(item.Id);
        domainEvent.AggregateVersion.ShouldBe(AggregateRoot.InitialVersion);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start);
    }
}

[Trait("Rule", "BR-INV-003")]
public sealed class InventoryItemAdjustmentTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_add_stock_and_append_a_ledger_movement_and_an_event()
    {
        var item = InventoryItems.With(_clock, onHand: 10);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = item.Adjust(5, "  Stocktake ", InventoryItems.Actor, "key-0001", _clock);

        result.IsSuccess.ShouldBeTrue();
        item.OnHand.ShouldBe(15);
        item.Version.ShouldBe(AggregateRoot.InitialVersion + 2);
        var movement = item.Movements.Last();
        movement.ShouldBeSameAs(result.Value);
        movement.InventoryItemId.ShouldBe(item.Id);
        movement.Delta.ShouldBe(5);
        movement.ReasonCode.ShouldBe("stocktake");
        movement.OnHandAfter.ShouldBe(15);
        movement.RecordedBy.ShouldBe(InventoryItems.Actor);
        movement.IdempotencyKey.ShouldBe("key-0001");
        movement.CreatedAt.ShouldBe(TestClock.Start.AddMinutes(1));
        var domainEvent = item.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StockAdjusted>();
        domainEvent.Delta.ShouldBe(5);
        domainEvent.OnHand.ShouldBe(15);
        domainEvent.ReasonCode.ShouldBe("stocktake");
        domainEvent.AggregateVersion.ShouldBe(item.Version);
    }

    [Fact]
    public void Should_remove_stock_down_to_the_reserved_quantity()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 4);

        var result = item.Adjust(-6, "damage", InventoryItems.Actor, null, _clock);

        result.IsSuccess.ShouldBeTrue();
        item.OnHand.ShouldBe(4);
        item.Available.ShouldBe(0);
    }

    [Fact]
    public void Should_append_one_movement_per_adjustment_without_touching_earlier_ones()
    {
        var item = InventoryItems.With(_clock);

        var first = item.Adjust(8, "stocktake", InventoryItems.Actor, "key-0001", _clock).Value;
        var second = item.Adjust(-3, "damage", InventoryItems.Actor, "key-0002", _clock).Value;

        item.Movements.ShouldBe([first, second]);
        first.Delta.ShouldBe(8);
        first.OnHandAfter.ShouldBe(8);
        second.OnHandAfter.ShouldBe(5);
    }

    [Fact]
    public void Should_reject_removing_stock_below_the_reserved_quantity_and_change_nothing()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 4);
        var versionBefore = item.Version;

        var result = item.Adjust(-7, "damage", InventoryItems.Actor, null, _clock);

        var error = result.ShouldFail();
        error.Code.ShouldBe("STOCK_BELOW_RESERVED");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe("delta");
        error.RuleId.ShouldBe("BR-INV-001");
        error.ShouldHaveParameters()["onHand"].ShouldBe(10);
        error.ShouldHaveParameters()["reserved"].ShouldBe(4);
        AssertUnchanged(item, onHand: 10, versionBefore);
    }

    [Fact]
    public void Should_reject_removing_stock_from_an_empty_item()
    {
        var item = InventoryItems.With(_clock);

        var result = item.Adjust(-1, "damage", InventoryItems.Actor, null, _clock);

        result.ShouldFail().Code.ShouldBe("STOCK_BELOW_RESERVED");
        item.OnHand.ShouldBe(0);
    }

    [Fact]
    public void Should_reject_stock_above_the_supported_maximum()
    {
        var item = InventoryItems.With(_clock);
        for (var added = 0; added < InventoryItem.MaxOnHand; added += InventoryItem.MaxAdjustmentMagnitude)
        {
            item.Adjust(InventoryItem.MaxAdjustmentMagnitude, "stocktake", InventoryItems.Actor, null, _clock)
                .IsSuccess.ShouldBeTrue();
        }

        item.ClearDomainEvents();
        var versionBefore = item.Version;
        var result = item.Adjust(1, "stocktake", InventoryItems.Actor, null, _clock);

        var error = result.ShouldFail();
        error.Code.ShouldBe("STOCK_ABOVE_MAXIMUM");
        error.RuleId.ShouldBe("BR-INV-001");
        AssertUnchanged(item, InventoryItem.MaxOnHand, versionBefore, movements: 100);
    }

    [Fact]
    public void Should_reject_an_adjustment_of_zero_units()
    {
        var item = InventoryItems.With(_clock, onHand: 5);
        var versionBefore = item.Version;

        var error = item.Adjust(0, "stocktake", InventoryItems.Actor, null, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_ADJUSTMENT_DELTA_ZERO");
        error.RuleId.ShouldBe("BR-INV-002");
        error.Field.ShouldBe("delta");
        AssertUnchanged(item, onHand: 5, versionBefore);
    }

    [Theory]
    [InlineData(InventoryItem.MaxAdjustmentMagnitude + 1)]
    [InlineData(-InventoryItem.MaxAdjustmentMagnitude - 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Should_reject_an_adjustment_larger_than_one_movement_may_carry(int delta)
    {
        var item = InventoryItems.With(_clock, onHand: 5);

        var error = item.Adjust(delta, "stocktake", InventoryItems.Actor, null, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_ADJUSTMENT_DELTA_RANGE");
        error.ShouldHaveParameters()["max"].ShouldBe(InventoryItem.MaxAdjustmentMagnitude);
        item.OnHand.ShouldBe(5);
    }

    [Fact]
    public void Should_accept_an_adjustment_of_exactly_the_maximum_magnitude()
    {
        var item = InventoryItems.With(_clock);

        item.Adjust(InventoryItem.MaxAdjustmentMagnitude, "stocktake", InventoryItems.Actor, null, _clock)
            .IsSuccess.ShouldBeTrue();

        item.OnHand.ShouldBe(InventoryItem.MaxAdjustmentMagnitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_reject_a_missing_reason_code(string? reason)
    {
        var item = InventoryItems.With(_clock, onHand: 5);

        var error = item.Adjust(1, reason, InventoryItems.Actor, null, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_REASON_REQUIRED");
        error.Field.ShouldBe("reasonCode");
        item.OnHand.ShouldBe(5);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1damage")]
    [InlineData("_damage")]
    [InlineData("dam age")]
    [InlineData("dam-age")]
    [InlineData("avaria_çao")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void Should_reject_a_malformed_reason_code(string reason)
    {
        var item = InventoryItems.With(_clock, onHand: 5);

        var error = item.Adjust(1, reason, InventoryItems.Actor, null, _clock).ShouldFail();

        error.Code.ShouldBe("STOCK_REASON_INVALID");
        error.ShouldHaveParameters()["min"].ShouldBe(StockMovement.MinReasonLength);
        error.ShouldHaveParameters()["max"].ShouldBe(StockMovement.MaxReasonLength);
        item.OnHand.ShouldBe(5);
    }

    private static void AssertUnchanged(InventoryItem item, int onHand, int versionBefore, int movements = 1)
    {
        item.OnHand.ShouldBe(onHand);
        item.Version.ShouldBe(versionBefore);
        item.DomainEvents.ShouldBeEmpty();
        item.Movements.Count.ShouldBe(movements);
    }
}

[Trait("Rule", "BR-INV-003")]
public sealed class StockMovementTests
{
    [Theory]
    [InlineData("stocktake", "stocktake")]
    [InlineData("  DAMAGE ", "damage")]
    [InlineData("return_2", "return_2")]
    public void Should_normalize_the_reason_code_to_trimmed_lowercase(string raw, string expected)
    {
        var reason = StockMovement.NormalizeReason(raw);

        reason.IsSuccess.ShouldBeTrue();
        reason.Value.ShouldBe(expected);
    }

    [Fact]
    public void Should_recognise_the_adjustment_it_recorded_and_no_other()
    {
        var clock = TestClock.Create();
        var movement = StockMovement.Record(Guid.CreateVersion7(), 5, "stocktake", 5, InventoryItems.Actor, "k", clock);

        movement.IsEquivalentTo(5, " Stocktake ").ShouldBeTrue();
        movement.IsEquivalentTo(6, "stocktake").ShouldBeFalse();
        movement.IsEquivalentTo(5, "damage").ShouldBeFalse();
        movement.IsEquivalentTo(5, null).ShouldBeFalse();
        movement.IsEquivalentTo(5, "1nvalid").ShouldBeFalse();
    }
}
