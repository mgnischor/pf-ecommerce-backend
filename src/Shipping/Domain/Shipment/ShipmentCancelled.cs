using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>Event raised when a shipment is cancelled because its order was cancelled before the carrier took it (BR-SHP-002).</summary>
internal sealed record ShipmentCancelled(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid OrderId
) : IDomainEvent
{
    /// <summary>Creates the event for the cancellation. Must be called after the state change.</summary>
    /// <param name="shipment">The cancelled shipment.</param>
    public static ShipmentCancelled For(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentCancelled(
            Guid.CreateVersion7(shipment.UpdatedAt),
            shipment.Id,
            shipment.Version,
            shipment.UpdatedAt,
            shipment.OrderId
        );
    }
}
