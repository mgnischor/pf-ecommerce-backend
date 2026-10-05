using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>
/// Event raised when the carrier takes a shipment (BR-SHP-002). Ordering moves the order to shipped on it.
/// The tracking code and the estimated delivery are present only when the carrier gave them.
/// </summary>
internal sealed record ShipmentDispatched(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string Carrier,
    string? TrackingCode,
    DateOnly? EstimatedDeliveryDate
) : IDomainEvent
{
    /// <summary>Creates the event for the dispatch. Must be called after the state change.</summary>
    /// <param name="shipment">The dispatched shipment.</param>
    public static ShipmentDispatched For(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentDispatched(
            Guid.CreateVersion7(shipment.UpdatedAt),
            shipment.Id,
            shipment.Version,
            shipment.UpdatedAt,
            shipment.OrderId,
            shipment.Carrier ?? string.Empty,
            shipment.TrackingCode,
            shipment.EstimatedDeliveryDate
        );
    }
}
