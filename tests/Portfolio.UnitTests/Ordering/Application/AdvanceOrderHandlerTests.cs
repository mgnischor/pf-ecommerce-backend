using Portfolio.Ordering.Application;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Ordering.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Application;

[Trait("Rule", "BR-ORD-003")]
public sealed class AdvanceOrderHandlerTests
{
    private const string Consumer = "ordering.test-consumer";

    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeInbox _inbox = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly AdvanceOrderHandler _handler;

    public AdvanceOrderHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_inbox);
        _handler = new AdvanceOrderHandler(_orders, _unitOfWork, _inbox, _clock);
    }

    private Order Seeded(OrderStatus status)
    {
        var order = OrderBuilder.New().WithStatus(status).Build(_clock);
        _orders.Seed(order);
        return order;
    }

    private Task<Result<bool>> AdvanceAsync(Guid orderId, OrderMilestone milestone, Guid? messageId = null) =>
        _handler.HandleAsync(
            new AdvanceOrderCommand(messageId ?? Guid.CreateVersion7(), Consumer, orderId, milestone),
            TestContext.Current.CancellationToken
        );

    [Theory]
    [InlineData("AwaitingPayment", "Paid", "Paid")]
    [InlineData("Paid", "Shipped", "Shipped")]
    [InlineData("Shipped", "Delivered", "Delivered")]
    public async Task Should_move_the_order_forward_and_persist_it(string from, string milestone, string expected)
    {
        var order = Seeded(Enum.Parse<OrderStatus>(from));

        var result = await AdvanceAsync(order.Id, Enum.Parse<OrderMilestone>(milestone));

        result.Value.ShouldBeTrue();
        order.Status.ShouldBe(Enum.Parse<OrderStatus>(expected));
        order.DomainEvents.ShouldHaveSingleItem();
        _orders.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_do_nothing_when_the_same_message_is_delivered_again()
    {
        var order = Seeded(OrderStatus.AwaitingPayment);
        var messageId = Guid.CreateVersion7();
        await AdvanceAsync(order.Id, OrderMilestone.Paid, messageId);

        var redelivery = await AdvanceAsync(order.Id, OrderMilestone.Paid, messageId);

        redelivery.Value.ShouldBeFalse();
        order.DomainEvents.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData("Paid", "Paid")]
    [InlineData("Shipped", "Paid")]
    [InlineData("Shipped", "Shipped")]
    [InlineData("Delivered", "Paid")]
    [InlineData("Delivered", "Shipped")]
    [InlineData("Delivered", "Delivered")]
    public async Task Should_accept_a_step_the_order_has_already_reached_and_change_nothing(
        string current,
        string milestone
    )
    {
        var order = Seeded(Enum.Parse<OrderStatus>(current));
        var versionBefore = order.Version;

        var result = await AdvanceAsync(order.Id, Enum.Parse<OrderMilestone>(milestone));

        result.Value.ShouldBeTrue();
        order.Status.ShouldBe(Enum.Parse<OrderStatus>(current));
        order.Version.ShouldBe(versionBefore);
        order.DomainEvents.ShouldBeEmpty();
        _orders.UpdateCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_still_commit_the_inbox_row_of_a_step_that_changed_nothing_so_it_is_not_handled_twice()
    {
        var order = Seeded(OrderStatus.Delivered);
        var messageId = Guid.CreateVersion7();

        await AdvanceAsync(order.Id, OrderMilestone.Shipped, messageId);
        var redelivery = await AdvanceAsync(order.Id, OrderMilestone.Shipped, messageId);

        redelivery.Value.ShouldBeFalse();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData("AwaitingPayment", "Shipped")]
    [InlineData("AwaitingPayment", "Delivered")]
    [InlineData("Paid", "Delivered")]
    [InlineData("Cancelled", "Paid")]
    [InlineData("Cancelled", "Shipped")]
    [InlineData("Cancelled", "Delivered")]
    public async Task Should_report_a_step_the_lifecycle_does_not_allow_as_a_conflict_and_persist_nothing(
        string current,
        string milestone
    )
    {
        var order = Seeded(Enum.Parse<OrderStatus>(current));

        var result = await AdvanceAsync(order.Id, Enum.Parse<OrderMilestone>(milestone));

        result.ShouldFail().Code.ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        order.Status.ShouldBe(Enum.Parse<OrderStatus>(current));
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_report_an_unknown_order_as_not_found_and_persist_nothing()
    {
        var result = await AdvanceAsync(Guid.CreateVersion7(), OrderMilestone.Paid);

        result.ShouldFail().ShouldBe(AdvanceOrderHandler.UnknownOrder);
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_keep_the_inboxes_of_different_consumers_apart_for_the_same_message()
    {
        var order = Seeded(OrderStatus.AwaitingPayment);
        var messageId = Guid.CreateVersion7();
        await AdvanceAsync(order.Id, OrderMilestone.Paid, messageId);

        var otherConsumer = await _handler.HandleAsync(
            new AdvanceOrderCommand(messageId, "ordering.another-consumer", order.Id, OrderMilestone.Paid),
            TestContext.Current.CancellationToken
        );

        otherConsumer.Value.ShouldBeTrue();
    }
}

[Trait("Rule", "BR-ORD-006")]
public sealed class GetOrderHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly GetOrderHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public GetOrderHandlerTests()
    {
        _handler = new GetOrderHandler(_orders);
    }

    [Fact]
    public async Task Should_return_the_order_with_its_price_snapshot_and_the_actions_its_state_allows()
    {
        var coffee = OrderBuilder.Line("CAF-600-PRT", 2, 189.90m);
        var order = OrderBuilder.New().ForCustomer(_customer).WithLines(coffee).Build(_clock);
        _orders.Seed(order);

        var result = await _handler.HandleAsync(
            new GetOrderQuery(_customer, order.Id),
            TestContext.Current.CancellationToken
        );

        var view = result.Value;
        (view.Id, view.Number, view.Status, view.TotalAmount, view.Currency).ShouldBe(
            (order.Id, order.Number, OrderStatusView.AwaitingPayment, 379.80m, "BRL")
        );
        var line = view.Items.ShouldHaveSingleItem();
        (line.ProductId, line.Sku, line.Quantity, line.UnitPriceAmount, line.LineTotalAmount).ShouldBe(
            (coffee.ProductId, "CAF-600-PRT", 2, 189.90m, 379.80m)
        );
        view.AllowedActions.ShouldBe([OrderActions.Cancel]);
        view.Version.ShouldBe(order.Version);
    }

    [Theory]
    [InlineData("AwaitingPayment", true)]
    [InlineData("Paid", true)]
    [InlineData("Shipped", false)]
    [InlineData("Delivered", false)]
    [InlineData("Cancelled", false)]
    public async Task Should_allow_cancelling_only_before_the_order_is_shipped(string status, bool canCancel)
    {
        var order = OrderBuilder.New().ForCustomer(_customer).WithStatus(Enum.Parse<OrderStatus>(status)).Build(_clock);
        _orders.Seed(order);

        var view = (
            await _handler.HandleAsync(new GetOrderQuery(_customer, order.Id), TestContext.Current.CancellationToken)
        ).Value;

        view.AllowedActions.Contains(OrderActions.Cancel).ShouldBe(canCancel);
    }

    [Fact]
    public async Task Should_answer_not_found_for_an_order_of_another_customer_exactly_like_a_missing_one()
    {
        var order = OrderBuilder.New().ForCustomer(Guid.CreateVersion7()).Build(_clock);
        _orders.Seed(order);

        var foreign = await _handler.HandleAsync(
            new GetOrderQuery(_customer, order.Id),
            TestContext.Current.CancellationToken
        );
        var missing = await _handler.HandleAsync(
            new GetOrderQuery(_customer, Guid.CreateVersion7()),
            TestContext.Current.CancellationToken
        );

        foreign.ShouldFail().ShouldBe(missing.ShouldFail());
        foreign.ShouldFail().Code.ShouldBe("ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Should_answer_not_found_for_a_deleted_order()
    {
        var order = OrderBuilder.New().ForCustomer(_customer).Build(_clock);
        _orders.Seed(order);
        _orders.Remove(order);

        var result = await _handler.HandleAsync(
            new GetOrderQuery(_customer, order.Id),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("ORDER_NOT_FOUND");
    }
}
