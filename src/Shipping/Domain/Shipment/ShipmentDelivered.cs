using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>Event raised when a shipment is delivered (BR-SHP-002). Ordering moves the order to delivered on it.</summary>
internal sealed record ShipmentDelivered(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid OrderId
) : IDomainEvent
{
    /// <summary>Creates the event for the delivery. Must be called after the state change.</summary>
    /// <param name="shipment">The delivered shipment.</param>
    public static ShipmentDelivered For(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentDelivered(
            Guid.CreateVersion7(shipment.UpdatedAt),
            shipment.Id,
            shipment.Version,
            shipment.UpdatedAt,
            shipment.OrderId
        );
    }
}
