using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>Event raised when a delivery fails or the shipment is returned (BR-SHP-002).</summary>
internal sealed record ShipmentFailed(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string ReasonCode
) : IDomainEvent
{
    /// <summary>Creates the event for the failure. Must be called after the state change.</summary>
    /// <param name="shipment">The failed shipment.</param>
    public static ShipmentFailed For(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentFailed(
            Guid.CreateVersion7(shipment.UpdatedAt),
            shipment.Id,
            shipment.Version,
            shipment.UpdatedAt,
            shipment.OrderId,
            shipment.FailureReason ?? string.Empty
        );
    }
}
