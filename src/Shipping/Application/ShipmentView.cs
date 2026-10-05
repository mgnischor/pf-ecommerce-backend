namespace Portfolio.Shipping.Application;

/// <summary>Read model of a shipment: what the API may show, without exposing the aggregate.</summary>
/// <param name="Id">Shipment identifier.</param>
/// <param name="OrderId">The order being fulfilled.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Carrier">Carrier name, or <c>null</c> until a carrier has the shipment.</param>
/// <param name="TrackingCode">Tracking code, or <c>null</c> until the carrier gives one.</param>
/// <param name="EstimatedDeliveryDate">Estimated delivery date, or <c>null</c>.</param>
internal sealed record ShipmentView(
    Guid Id,
    Guid OrderId,
    ShipmentStatusView Status,
    string? Carrier,
    string? TrackingCode,
    DateOnly? EstimatedDeliveryDate
);
