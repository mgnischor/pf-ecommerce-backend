using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>Event raised when an order is delivered (BR-ORD-003). Terminal.</summary>
internal sealed record OrderDelivered(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid CustomerId,
    string Number
) : IDomainEvent
{
    /// <summary>Creates the event for the delivery. Must be called after the state change.</summary>
    /// <param name="order">The delivered order.</param>
    public static OrderDelivered For(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderDelivered(
            Guid.CreateVersion7(order.UpdatedAt),
            order.Id,
            order.Version,
            order.UpdatedAt,
            order.CustomerId,
            order.Number
        );
    }
}
