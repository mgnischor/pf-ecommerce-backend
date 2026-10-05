using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>Event raised when an order is handed to the carrier (BR-ORD-003).</summary>
internal sealed record OrderShipped(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid CustomerId,
    string Number
) : IDomainEvent
{
    /// <summary>Creates the event for the hand-over. Must be called after the state change.</summary>
    /// <param name="order">The shipped order.</param>
    public static OrderShipped For(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderShipped(
            Guid.CreateVersion7(order.UpdatedAt),
            order.Id,
            order.Version,
            order.UpdatedAt,
            order.CustomerId,
            order.Number
        );
    }
}
