using Portfolio.Ordering.Application;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Ordering.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Application;

public sealed class PlaceOrderHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PlaceOrderHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public PlaceOrderHandlerTests()
    {
        _handler = new PlaceOrderHandler(_orders, _unitOfWork, _clock);
    }

    private static PlaceOrderLine Line(
        string sku = "CAF-600-PRT",
        int quantity = 2,
        decimal price = 189.90m,
        string? currency = "BRL"
    ) => new(Guid.CreateVersion7(), sku, "Cafeteira Elétrica", quantity, price, currency);

    private Task<Result<OrderView>> PlaceAsync(IReadOnlyList<PlaceOrderLine>? lines, Guid? checkout = null) =>
        _handler.HandleAsync(
            new PlaceOrderCommand(checkout ?? Guid.CreateVersion7(), _customer, lines),
            TestContext.Current.CancellationToken
        );

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public async Task Should_place_the_order_with_a_number_and_a_server_computed_total_and_persist_it()
    {
        var result = await PlaceAsync([Line(quantity: 2, price: 189.90m), Line("MOE-100-PRT", 1, 49.50m)]);

        var view = result.Value;
        view.Number.ShouldBe("PF-2026-000001");
        view.Status.ShouldBe(OrderStatusView.AwaitingPayment);
        view.TotalAmount.ShouldBe(429.30m);
        view.Currency.ShouldBe("BRL");
        view.Items.Count.ShouldBe(2);
        view.PlacedAt.ShouldBe(TestClock.Start);
        view.Version.ShouldBe(AggregateRoot.InitialVersion);
        view.AllowedActions.ShouldBe([OrderActions.Cancel]);
        _orders.Orders.ShouldHaveSingleItem().DomainEvents.OfType<OrderPlaced>().ShouldHaveSingleItem();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-ORD-005")]
    public async Task Should_give_each_order_the_next_number()
    {
        var first = await PlaceAsync([Line()]);
        var second = await PlaceAsync([Line()]);

        (first.Value.Number, second.Value.Number).ShouldBe(("PF-2026-000001", "PF-2026-000002"));
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public async Task Should_answer_a_second_placement_of_the_same_checkout_with_the_order_that_exists()
    {
        var checkout = Guid.CreateVersion7();
        var first = await PlaceAsync([Line()], checkout);

        var again = await PlaceAsync([Line("OTHER-0001", 5, 1m)], checkout);

        again.IsSuccess.ShouldBeTrue();
        again.Value.Id.ShouldBe(first.Value.Id);
        again.Value.TotalAmount.ShouldBe(first.Value.TotalAmount);
        _orders.Orders.Count.ShouldBe(1);
        _orders.NumbersDrawn.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("BR")]
    [InlineData("B2N")]
    public async Task Should_reject_a_line_with_an_invalid_currency_and_persist_nothing(string? currency)
    {
        var result = await PlaceAsync([Line(currency: currency)]);

        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
        _orders.Orders.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public async Task Should_reject_an_order_without_lines_and_persist_nothing()
    {
        var empty = await PlaceAsync([]);
        var missing = await PlaceAsync(null);

        empty.ShouldFail().Code.ShouldBe("ORDER_NO_ITEMS");
        missing.ShouldFail().Code.ShouldBe("ORDER_NO_ITEMS");
        _orders.Orders.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 10)]
    [InlineData(1, 0)]
    [Trait("Rule", "BR-ORD-001")]
    public async Task Should_reject_a_line_outside_the_allowed_quantity_and_price_and_persist_nothing(
        int quantity,
        int price
    )
    {
        var result = await PlaceAsync([Line(quantity: quantity, price: price)]);

        result.IsFailure.ShouldBeTrue();
        _orders.Orders.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_lines_priced_in_different_currencies()
    {
        var result = await PlaceAsync([Line(currency: "BRL"), Line("SKU-0002", currency: "USD")]);

        result.ShouldFail().Code.ShouldBe("ORDER_CURRENCY_MIXED");
    }
}

public sealed class CancelOrderHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CancelOrderHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public CancelOrderHandlerTests()
    {
        _handler = new CancelOrderHandler(_orders, _unitOfWork, _clock);
    }

    private Order Seeded(OrderStatus status = OrderStatus.AwaitingPayment)
    {
        var order = OrderBuilder.New().ForCustomer(_customer).WithStatus(status).Build(_clock);
        _orders.Seed(order);
        return order;
    }

    private Task<Result<OrderView>> CancelAsync(
        Order order,
        string key = "key-0001",
        int? version = null,
        string? reason = "changedMind",
        string? note = null,
        Guid? customer = null
    ) =>
        _handler.HandleAsync(
            new CancelOrderCommand(customer ?? _customer, order.Id, key, version ?? order.Version, reason, note),
            TestContext.Current.CancellationToken
        );

    [Fact]
    [Trait("Rule", "BR-ORD-004")]
    public async Task Should_cancel_the_order_persist_it_and_return_the_new_version_without_actions()
    {
        var order = Seeded();

        var result = await CancelAsync(order);

        result.Value.Status.ShouldBe(OrderStatusView.Cancelled);
        result.Value.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        result.Value.AllowedActions.ShouldBeEmpty();
        order.DomainEvents.OfType<OrderCancelled>().ShouldHaveSingleItem();
        _orders.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-ORD-004")]
    public async Task Should_answer_a_retry_of_the_same_cancellation_with_the_cancelled_order_and_cancel_nothing_twice()
    {
        var order = Seeded();
        var versionRead = order.Version;
        await CancelAsync(order, note: "nota");

        // The retry carries the version the original read, which the original already advanced.
        var retry = await CancelAsync(order, version: versionRead, note: "nota");

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Status.ShouldBe(OrderStatusView.Cancelled);
        order.DomainEvents.OfType<OrderCancelled>().Count().ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData("foundCheaper", null)]
    [InlineData("changedMind", "outra nota")]
    [Trait("Rule", "BR-ORD-004")]
    public async Task Should_reject_the_same_key_for_a_different_cancellation_with_a_422(string reason, string? note)
    {
        var order = Seeded();
        await CancelAsync(order, note: "nota");

        var reuse = await CancelAsync(order, reason: reason, note: note);

        reuse.ShouldFail().Code.ShouldBe("IDEMPOTENCY_KEY_REUSED");
        reuse.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        order.CancellationReason.ShouldBe("changedMind");
    }

    [Fact]
    public async Task Should_refuse_a_second_cancellation_under_another_key_because_the_order_is_already_cancelled()
    {
        var order = Seeded();
        var versionRead = order.Version;
        await CancelAsync(order, "key-0001");

        // The version the client read is stale: the order moved on when it was cancelled.
        var other = await CancelAsync(order, "key-0002", versionRead);

        other.ShouldFail().Type.ShouldBe(ErrorType.PreconditionFailed);
        var fresh = await CancelAsync(order, "key-0002", order.Version);
        fresh.ShouldFail().Code.ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        fresh.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task Should_fail_the_precondition_and_change_nothing_when_the_version_is_stale_or_malformed(int? stale)
    {
        var order = Seeded();
        var command = new CancelOrderCommand(_customer, order.Id, "key-0001", stale, "changedMind", null);

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("ORDER_VERSION_MISMATCH");
        result.ShouldFail().Type.ShouldBe(ErrorType.PreconditionFailed);
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    [Trait("Rule", "BR-ORD-003")]
    public async Task Should_answer_a_conflict_for_an_order_that_can_no_longer_be_cancelled(string statusName)
    {
        var order = Seeded(Enum.Parse<OrderStatus>(statusName));

        var result = await CancelAsync(order);

        result.ShouldFail().Code.ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_answer_not_found_for_an_order_of_another_customer_exactly_like_a_missing_one()
    {
        var order = Seeded();

        var foreign = await CancelAsync(order, customer: Guid.CreateVersion7());
        var missing = await _handler.HandleAsync(
            new CancelOrderCommand(_customer, Guid.CreateVersion7(), "key-0001", 1, "changedMind", null),
            TestContext.Current.CancellationToken
        );

        foreign.ShouldFail().ShouldBe(missing.ShouldFail());
        foreign.ShouldFail().Code.ShouldBe("ORDER_NOT_FOUND");
        foreign.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_not_recognise_a_retry_made_by_another_customer_even_with_the_same_key()
    {
        var order = Seeded();
        await CancelAsync(order);

        var intruder = await CancelAsync(order, customer: Guid.CreateVersion7());

        intruder.ShouldFail().Code.ShouldBe("ORDER_NOT_FOUND");
    }

    [Theory]
    [InlineData(null, "ORDER_REASON_REQUIRED")]
    [InlineData("1", "ORDER_REASON_INVALID")]
    public async Task Should_reject_an_invalid_reason_and_persist_nothing(string? reason, string code)
    {
        var order = Seeded();

        var result = await CancelAsync(order, reason: reason);

        result.ShouldFail().Code.ShouldBe(code);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
