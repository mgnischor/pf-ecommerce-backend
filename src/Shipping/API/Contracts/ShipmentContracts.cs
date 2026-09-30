namespace Portfolio.Shipping.API.Contracts;

/// <summary>Status of a shipment. An open set: clients must tolerate new values.</summary>
public enum ShipmentStatusContract
{
    /// <summary>Being prepared.</summary>
    Preparing,

    /// <summary>With the carrier.</summary>
    InTransit,

    /// <summary>Delivered.</summary>
    Delivered,

    /// <summary>Delivery failed or was returned.</summary>
    Failed,
}

/// <summary>Shipment resource.</summary>
/// <param name="Id">Shipment identifier.</param>
/// <param name="OrderId">Order being fulfilled.</param>
/// <param name="Status">Shipment status.</param>
/// <param name="Carrier">Carrier name, omitted until one is assigned.</param>
/// <param name="TrackingCode">Carrier tracking code, omitted until available.</param>
/// <param name="EstimatedDeliveryDate">Estimated delivery as a calendar date (<c>YYYY-MM-DD</c>).</param>
public sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    ShipmentStatusContract Status,
    string? Carrier,
    string? TrackingCode,
    DateOnly? EstimatedDeliveryDate
);
