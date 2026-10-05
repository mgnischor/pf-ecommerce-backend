using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Ordering.Domain;

/// <summary>
/// Builds valid orders with sensible defaults; tests override only what they assert on (ai/TESTS.md §10). The returned
/// order has no pending domain events, so assertions see only what the test provokes.
/// </summary>
internal sealed class OrderBuilder
{
    private Guid _customerId = Guid.CreateVersion7();
    private Guid _checkoutId = Guid.CreateVersion7();
    private string _number = "PF-2026-000001";
    private OrderStatus _status = OrderStatus.AwaitingPayment;
    private List<OrderLine> _lines =
    [
        new(Guid.CreateVersion7(), "CAF-600-PRT", "Cafeteira Elétrica", 2, new Money(189.90m, "BRL")),
        new(Guid.CreateVersion7(), "MOE-100-PRT", "Moedor de Café", 1, new Money(49.50m, "BRL")),
    ];

    public static OrderBuilder New() => new();

    public static OrderLine Line(
        string sku = "CAF-600-PRT",
        int quantity = 1,
        decimal price = 100m,
        string currency = "BRL",
        string name = "Cafeteira Elétrica"
    ) => new(Guid.CreateVersion7(), sku, name, quantity, new Money(price, currency));

    public OrderBuilder ForCustomer(Guid customerId)
    {
        _customerId = customerId;
        return this;
    }

    public OrderBuilder FromCheckout(Guid checkoutId)
    {
        _checkoutId = checkoutId;
        return this;
    }

    public OrderBuilder Numbered(string number)
    {
        _number = number;
        return this;
    }

    public OrderBuilder WithLines(params OrderLine[] lines)
    {
        _lines = [.. lines];
        return this;
    }

    public OrderBuilder WithStatus(OrderStatus status)
    {
        _status = status;
        return this;
    }

    public Order Build(TimeProvider timeProvider)
    {
        var order = Order.Place(_checkoutId, _customerId, _number, _lines, timeProvider).Value;

        if (_status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            order.MarkPaid(timeProvider);
        }

        if (_status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            order.MarkShipped(timeProvider);
        }

        if (_status is OrderStatus.Delivered)
        {
            order.MarkDelivered(timeProvider);
        }

        if (_status is OrderStatus.Cancelled)
        {
            order.Cancel("changedMind", null, "key-setup-0001", timeProvider);
        }

        order.ClearDomainEvents();
        return order;
    }
}
