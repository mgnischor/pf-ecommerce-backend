using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>Event raised when an order is paid (BR-ORD-003). Fulfillment starts on it.</summary>
internal sealed record OrderPaid(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid CustomerId,
    string Number
) : IDomainEvent
{
    /// <summary>Creates the event for the payment. Must be called after the state change.</summary>
    /// <param name="order">The paid order.</param>
    public static OrderPaid For(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderPaid(
            Guid.CreateVersion7(order.UpdatedAt),
            order.Id,
            order.Version,
            order.UpdatedAt,
            order.CustomerId,
            order.Number
        );
    }
}
