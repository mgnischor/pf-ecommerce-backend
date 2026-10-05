using Portfolio.Ordering.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>Projects the aggregate onto its read models.</summary>
internal static class OrderMapping
{
    /// <summary>Builds the full view of <paramref name="order"/>.</summary>
    /// <param name="order">Order, loaded with its lines.</param>
    public static OrderView ToView(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderView(
            order.Id,
            order.Number,
            order.Status.ToView(),
            [.. order.Items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).Select(ToView)],
            order.Total.Amount,
            order.Total.Currency,
            order.PlacedAt,
            order.Version,
            AllowedActionsOf(order.Status)
        );
    }

    /// <summary>Maps a domain status to the status of the read models.</summary>
    /// <param name="status">Domain status.</param>
    public static OrderStatusView ToView(this OrderStatus status) =>
        status switch
        {
            OrderStatus.AwaitingPayment => OrderStatusView.AwaitingPayment,
            OrderStatus.Paid => OrderStatusView.Paid,
            OrderStatus.Shipped => OrderStatusView.Shipped,
            OrderStatus.Delivered => OrderStatusView.Delivered,
            OrderStatus.Cancelled => OrderStatusView.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status."),
        };

    /// <summary>Builds the collection view of <paramref name="order"/>.</summary>
    /// <param name="order">Order.</param>
    public static OrderSummaryView ToSummary(this Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderSummaryView(
            order.Id,
            order.Number,
            order.Status.ToView(),
            order.Total.Amount,
            order.Total.Currency,
            order.PlacedAt
        );
    }

    /// <summary>Maps a read-model status back to the domain status.</summary>
    /// <param name="status">Read-model status.</param>
    public static OrderStatus ToDomain(this OrderStatusView status) =>
        status switch
        {
            OrderStatusView.AwaitingPayment => OrderStatus.AwaitingPayment,
            OrderStatusView.Paid => OrderStatus.Paid,
            OrderStatusView.Shipped => OrderStatus.Shipped,
            OrderStatusView.Delivered => OrderStatus.Delivered,
            OrderStatusView.Cancelled => OrderStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status."),
        };

    private static OrderItemView ToView(OrderItem item) =>
        new(
            item.ProductId,
            item.Sku,
            item.Name,
            item.Quantity,
            item.UnitPrice.Amount,
            item.LineTotal.Amount,
            item.UnitPrice.Currency
        );

    private static string[] AllowedActionsOf(OrderStatus status) =>
        status is OrderStatus.AwaitingPayment or OrderStatus.Paid ? [OrderActions.Cancel] : [];
}
