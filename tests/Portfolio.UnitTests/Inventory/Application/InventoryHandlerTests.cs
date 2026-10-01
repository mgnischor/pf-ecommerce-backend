using Portfolio.Inventory.Application;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Inventory.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Inventory.Application;

[Trait("Rule", "BR-INV-008")]
public sealed class OpenInventoryItemHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeInventoryItemRepository _items = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly OpenInventoryItemHandler _handler;

    public OpenInventoryItemHandlerTests()
    {
        _handler = new OpenInventoryItemHandler(_items, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Should_open_an_empty_item_and_persist_it()
    {
        var result = await _handler.HandleAsync(
            new OpenInventoryItemCommand(" caf-600-prt "),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new InventoryItemView("CAF-600-PRT", 0, 0, 0, AggregateRoot.InitialVersion));
        _items.Items.ShouldHaveSingleItem().Sku.Value.ShouldBe("CAF-600-PRT");
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_reject_a_second_item_for_the_same_sku_as_a_conflict_without_duplicating_it()
    {
        var command = new OpenInventoryItemCommand("CAF-600-PRT");
        await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var replay = await _handler.HandleAsync(
            command with
            {
                Sku = "caf-600-prt",
            },
            TestContext.Current.CancellationToken
        );

        var error = replay.ShouldFail();
        error.Code.ShouldBe("INVENTORY_ITEM_ALREADY_EXISTS");
        error.Type.ShouldBe(ErrorType.Conflict);
        error.RuleId.ShouldBe("BR-INV-008");
        _items.Items.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_reject_an_invalid_sku_and_persist_nothing()
    {
        var result = await _handler.HandleAsync(
            new OpenInventoryItemCommand("no"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("INVENTORY_SKU_LENGTH");
        _items.Items.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}

[Trait("Rule", "BR-INV-004")]
public sealed class GetInventoryItemHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeInventoryItemRepository _items = new();
    private readonly GetInventoryItemHandler _handler;

    public GetInventoryItemHandlerTests()
    {
        _handler = new GetInventoryItemHandler(_items);
    }

    [Fact]
    public async Task Should_return_the_stock_level_with_available_computed()
    {
        var item = InventoryItems.With(_clock, onHand: 10, reserved: 4);
        _items.Seed(item);

        var result = await _handler.HandleAsync(
            new GetInventoryItemQuery("caf-600-prt"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new InventoryItemView("CAF-600-PRT", 10, 4, 6, item.Version));
    }

    [Fact]
    public async Task Should_report_not_found_when_the_sku_has_no_item()
    {
        var result = await _handler.HandleAsync(
            new GetInventoryItemQuery("CAF-600-PRT"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("INVENTORY_ITEM_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_reject_an_invalid_sku()
    {
        var result = await _handler.HandleAsync(
            new GetInventoryItemQuery("not a sku"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("INVENTORY_SKU_INVALID_CHARACTERS");
    }
}

[Trait("Rule", "BR-INV-003")]
public sealed class AdjustStockHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeInventoryItemRepository _items = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AdjustStockHandler _handler;

    public AdjustStockHandlerTests()
    {
        _handler = new AdjustStockHandler(_items, _unitOfWork, _clock);
    }

    private static AdjustStockCommand Command(
        InventoryItemSeed seed,
        int delta = 5,
        string reason = "stocktake",
        string key = "key-0001",
        int? version = null
    ) => new(InventoryItems.Actor, seed.Sku, delta, reason, key, version ?? seed.Version);

    private InventoryItemSeed Seed(int onHand = 10, int reserved = 0)
    {
        var item = InventoryItems.With(_clock, onHand, reserved);
        _items.Seed(item);
        return new InventoryItemSeed(item, item.Sku.Value, item.Version);
    }

    [Fact]
    public async Task Should_adjust_the_stock_record_the_movement_and_persist_it()
    {
        var seed = Seed(onHand: 10);

        var result = await _handler.HandleAsync(
            Command(seed, delta: -3, reason: "damage"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new InventoryItemView("CAF-600-PRT", 7, 0, 7, seed.Version + 1));
        var movement = seed.Item.Movements.Last();
        movement.Delta.ShouldBe(-3);
        movement.RecordedBy.ShouldBe(InventoryItems.Actor);
        movement.IdempotencyKey.ShouldBe("key-0001");
        _items.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_answer_a_retry_with_the_same_key_without_adjusting_twice()
    {
        var seed = Seed(onHand: 10);
        var command = Command(seed);
        var first = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // The retry still carries the version the original request read, which the original already advanced.
        var retry = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.ShouldBe(first.Value);
        seed.Item.OnHand.ShouldBe(15);
        seed.Item.Movements.Count(movement => movement.IdempotencyKey == "key-0001").ShouldBe(1);
        seed.Item.DomainEvents.OfType<StockAdjusted>().Count().ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_treat_a_retry_that_only_differs_in_reason_casing_as_the_same_adjustment()
    {
        var seed = Seed(onHand: 10);
        await _handler.HandleAsync(Command(seed, reason: "stocktake"), TestContext.Current.CancellationToken);

        var retry = await _handler.HandleAsync(
            Command(seed, reason: " StockTake "),
            TestContext.Current.CancellationToken
        );

        retry.IsSuccess.ShouldBeTrue();
        seed.Item.OnHand.ShouldBe(15);
    }

    [Theory]
    [InlineData(6, "stocktake")]
    [InlineData(5, "damage")]
    public async Task Should_reject_reusing_a_key_for_a_different_adjustment_and_change_nothing(
        int delta,
        string reason
    )
    {
        var seed = Seed(onHand: 10);
        await _handler.HandleAsync(Command(seed), TestContext.Current.CancellationToken);

        var reuse = await _handler.HandleAsync(Command(seed, delta, reason), TestContext.Current.CancellationToken);

        var error = reuse.ShouldFail();
        error.Code.ShouldBe("IDEMPOTENCY_KEY_REUSED");
        error.Type.ShouldBe(ErrorType.Conflict);
        seed.Item.OnHand.ShouldBe(15);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_allow_the_same_key_on_a_different_item()
    {
        var seed = Seed(onHand: 10);
        var other = InventoryItems.With(_clock, onHand: 1, sku: "FIL-100-PAP");
        _items.Seed(other);
        await _handler.HandleAsync(Command(seed), TestContext.Current.CancellationToken);

        var result = await _handler.HandleAsync(
            new AdjustStockCommand(InventoryItems.Actor, "FIL-100-PAP", 2, "stocktake", "key-0001", other.Version),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        other.OnHand.ShouldBe(3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Should_reject_a_stale_version_as_a_precondition_failure_and_persist_nothing(int offset)
    {
        var seed = Seed(onHand: 10);

        var result = await _handler.HandleAsync(
            Command(seed, version: seed.Version + offset),
            TestContext.Current.CancellationToken
        );

        var error = result.ShouldFail();
        error.Code.ShouldBe("INVENTORY_VERSION_MISMATCH");
        error.Type.ShouldBe(ErrorType.PreconditionFailed);
        seed.Item.OnHand.ShouldBe(10);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_missing_version_as_a_precondition_failure()
    {
        var seed = Seed(onHand: 10);

        var result = await _handler.HandleAsync(
            Command(seed) with
            {
                ExpectedVersion = null,
            },
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Type.ShouldBe(ErrorType.PreconditionFailed);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_report_not_found_and_persist_nothing_when_the_sku_has_no_item()
    {
        var result = await _handler.HandleAsync(
            new AdjustStockCommand(InventoryItems.Actor, "NOP-000-XXX", 1, "stocktake", "key-0001", 1),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("INVENTORY_ITEM_NOT_FOUND");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_surface_a_violated_rule_and_persist_nothing()
    {
        var seed = Seed(onHand: 10, reserved: 8);

        var result = await _handler.HandleAsync(
            Command(seed, delta: -3, reason: "damage"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("STOCK_BELOW_RESERVED");
        seed.Item.OnHand.ShouldBe(10);
        _items.UpdateCalls.ShouldBe(0);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_let_the_client_retry_after_a_rule_violation_with_the_same_key()
    {
        var seed = Seed(onHand: 10, reserved: 8);
        await _handler.HandleAsync(Command(seed, delta: -3, reason: "damage"), TestContext.Current.CancellationToken);

        var corrected = await _handler.HandleAsync(
            Command(seed, delta: -2, reason: "damage"),
            TestContext.Current.CancellationToken
        );

        corrected.IsSuccess.ShouldBeTrue();
        seed.Item.OnHand.ShouldBe(8);
    }

    [Fact]
    public async Task Should_reject_an_invalid_sku()
    {
        var result = await _handler.HandleAsync(
            new AdjustStockCommand(InventoryItems.Actor, "no", 1, "stocktake", "key-0001", 1),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("INVENTORY_SKU_LENGTH");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    private sealed record InventoryItemSeed(InventoryItem Item, string Sku, int Version);
}
