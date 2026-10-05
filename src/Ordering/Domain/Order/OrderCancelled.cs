using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// Event raised when an order is cancelled (BR-ORD-004). <c>WasPaid</c> tells the consumers whether money has to be
/// returned and whether a shipment may already exist. The free-text note is deliberately not part of it (ai/SECURITY.md §11.2).
/// </summary>
internal sealed record OrderCancelled(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid CustomerId,
    string Number,
    string ReasonCode,
    bool WasPaid
) : IDomainEvent
{
    /// <summary>Creates the event for the cancellation. Must be called after the state change.</summary>
    /// <param name="order">The cancelled order.</param>
    /// <param name="wasPaid">Whether the order had been paid when it was cancelled.</param>
    public static OrderCancelled For(Order order, bool wasPaid)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderCancelled(
            Guid.CreateVersion7(order.UpdatedAt),
            order.Id,
            order.Version,
            order.UpdatedAt,
            order.CustomerId,
            order.Number,
            order.CancellationReason ?? string.Empty,
            wasPaid
        );
    }
}
