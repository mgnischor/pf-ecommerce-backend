namespace Portfolio.Shipping.Application;

/// <summary>Request to record that the carrier took a shipment (BR-SHP-002, BR-SHP-003).</summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Carrier">Carrier name.</param>
/// <param name="TrackingCode">Tracking code, if the carrier gave one.</param>
/// <param name="EstimatedDeliveryDate">Estimated delivery date, if the carrier gave one.</param>
internal sealed record DispatchShipmentCommand(
    Guid ShipmentId,
    string? Carrier,
    string? TrackingCode,
    DateOnly? EstimatedDeliveryDate
);
