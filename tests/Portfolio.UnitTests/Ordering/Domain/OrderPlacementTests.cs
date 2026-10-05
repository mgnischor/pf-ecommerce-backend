using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Domain;

public sealed class OrderPlacementTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly Guid _customer = Guid.CreateVersion7();
    private readonly Guid _checkout = Guid.CreateVersion7();

    private Result<Order> Place(
        IReadOnlyCollection<OrderLine>? lines,
        string? number = "PF-2026-000001",
        Guid? customer = null,
        Guid? checkout = null
    ) => Order.Place(checkout ?? _checkout, customer ?? _customer, number, lines, _clock);

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_place_an_order_awaiting_payment_with_the_snapshot_of_its_lines()
    {
        var coffee = OrderBuilder.Line("CAF-600-PRT", 2, 189.90m, name: "  Cafeteira Elétrica ");
        var grinder = OrderBuilder.Line("MOE-100-PRT", 1, 49.50m, name: "Moedor de Café");

        var order = Place([coffee, grinder]).Value;

        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        order.CustomerId.ShouldBe(_customer);
        order.CheckoutId.ShouldBe(_checkout);
        order.Number.ShouldBe("PF-2026-000001");
        order.Version.ShouldBe(AggregateRoot.InitialVersion);
        order.PlacedAt.ShouldBe(TestClock.Start);
        order.Items.Count.ShouldBe(2);
        var line = order.Items.Single(item => item.ProductId == coffee.ProductId);
        (line.OrderId, line.Sku, line.Name, line.Quantity, line.UnitPrice).ShouldBe(
            (order.Id, "CAF-600-PRT", "Cafeteira Elétrica", 2, new Money(189.90m, "BRL"))
        );
    }

    [Fact]
    [Trait("Rule", "BR-ORD-002")]
    public void Should_compute_the_total_from_the_lines_and_never_take_it_from_the_caller()
    {
        var order = Place([
            OrderBuilder.Line(quantity: 2, price: 189.90m),
            OrderBuilder.Line(quantity: 3, price: 49.50m),
        ]).Value;

        order.Total.ShouldBe(new Money(528.30m, "BRL"));
        order.Items.Select(item => item.LineTotal.Amount).Order().ShouldBe([148.50m, 379.80m]);
    }

    [Fact]
    [Trait("Rule", "BR-ORD-002")]
    public void Should_round_each_amount_with_the_money_rounding_policy()
    {
        var order = Place([OrderBuilder.Line(quantity: 3, price: 0.3333m)]).Value;

        order.Total.Amount.ShouldBe(0.9999m);
    }

    [Fact]
    public void Should_raise_a_single_placed_event_carrying_the_full_snapshot()
    {
        var coffee = OrderBuilder.Line("CAF-600-PRT", 2, 189.90m);

        var order = Place([coffee]).Value;

        var domainEvent = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>();
        domainEvent.AggregateId.ShouldBe(order.Id);
        domainEvent.AggregateVersion.ShouldBe(AggregateRoot.InitialVersion);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start);
        domainEvent.CustomerId.ShouldBe(_customer);
        domainEvent.Number.ShouldBe("PF-2026-000001");
        domainEvent.Total.ShouldBe(new Money(379.80m, "BRL"));
        domainEvent
            .Items.ShouldHaveSingleItem()
            .ShouldBe(new OrderPlacedItem(coffee.ProductId, "CAF-600-PRT", 2, new Money(189.90m, "BRL")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PF-2026-0000000000000000000000000000001")]
    [Trait("Rule", "BR-ORD-005")]
    public void Should_require_an_order_number_of_a_bounded_length(string? number)
    {
        var result = Place([OrderBuilder.Line()], number);

        result.ShouldFail().Code.ShouldBe("ORDER_NUMBER_REQUIRED");
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_require_a_customer_and_a_checkout()
    {
        Place([OrderBuilder.Line()], customer: Guid.Empty).ShouldFail().Code.ShouldBe("ORDER_CUSTOMER_REQUIRED");
        Place([OrderBuilder.Line()], checkout: Guid.Empty).ShouldFail().Code.ShouldBe("ORDER_CUSTOMER_REQUIRED");
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_require_at_least_one_line()
    {
        Place([]).ShouldFail().Code.ShouldBe("ORDER_NO_ITEMS");
        Place(null).ShouldFail().Code.ShouldBe("ORDER_NO_ITEMS");
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_accept_the_maximum_number_of_lines_and_refuse_one_more()
    {
        var full = Enumerable.Range(0, Order.MaxItems).Select(index => OrderBuilder.Line($"SKU-{index:0000}")).ToList();

        Place(full).IsSuccess.ShouldBeTrue();
        Place([.. full, OrderBuilder.Line("SKU-9999")]).ShouldFail().Code.ShouldBe("ORDER_TOO_MANY_ITEMS");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_reject_a_line_quantity_outside_one_to_ninety_nine(int quantity)
    {
        var result = Place([OrderBuilder.Line(quantity: quantity)]);

        result.ShouldFail().Code.ShouldBe("ORDER_ITEM_QUANTITY_INVALID");
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_reject_a_line_that_is_not_priced_above_zero(int price)
    {
        Place([OrderBuilder.Line(price: price)]).ShouldFail().Code.ShouldBe("ORDER_ITEM_INVALID");
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_reject_a_line_missing_its_product_sku_or_name()
    {
        var noProduct = new OrderLine(Guid.Empty, "CAF-600-PRT", "Cafeteira", 1, new Money(1m, "BRL"));
        var noSku = new OrderLine(Guid.CreateVersion7(), " ", "Cafeteira", 1, new Money(1m, "BRL"));
        var noName = new OrderLine(Guid.CreateVersion7(), "CAF-600-PRT", "", 1, new Money(1m, "BRL"));
        var longName = new OrderLine(
            Guid.CreateVersion7(),
            "CAF-600-PRT",
            new string('x', 201),
            1,
            new Money(1m, "BRL")
        );

        foreach (var line in new[] { noProduct, noSku, noName, longName })
        {
            Place([line]).ShouldFail().Code.ShouldBe("ORDER_ITEM_INVALID");
        }
    }

    [Fact]
    [Trait("Rule", "BR-ORD-001")]
    public void Should_reject_lines_priced_in_different_currencies()
    {
        var result = Place([OrderBuilder.Line(currency: "BRL"), OrderBuilder.Line("SKU-0002", currency: "USD")]);

        result.ShouldFail().Code.ShouldBe("ORDER_CURRENCY_MIXED");
    }

    [Fact]
    public void Should_not_place_anything_when_a_rule_is_violated()
    {
        var result = Place([OrderBuilder.Line(quantity: 0)]);

        result.IsFailure.ShouldBeTrue();
    }
}
