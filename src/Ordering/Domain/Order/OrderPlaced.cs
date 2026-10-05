using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// Event raised when an order is placed (BR-ORD-001). Carries the full snapshot (customer, number, total, lines) so
/// consumers such as billing or inventory never need to query back.
/// </summary>
internal sealed record OrderPlaced(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid CustomerId,
    string Number,
    Money Total,
    IReadOnlyList<OrderPlacedItem> Items
) : IDomainEvent
{
    /// <summary>Creates the event for a newly placed order. Must be called after the state change.</summary>
    /// <param name="order">The placed order.</param>
    public static OrderPlaced For(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderPlaced(
            Guid.CreateVersion7(order.CreatedAt),
            order.Id,
            order.Version,
            order.CreatedAt,
            order.CustomerId,
            order.Number,
            order.Total,
            [
                .. order.Items.Select(item => new OrderPlacedItem(
                    item.ProductId,
                    item.Sku,
                    item.Quantity,
                    item.UnitPrice
                )),
            ]
        );
    }
}
