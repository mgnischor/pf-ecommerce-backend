using System.Text.Json.Serialization;

namespace Portfolio.Shipping.API.Contracts;

/// <summary>Status of a shipment. An open set: clients must tolerate new values.</summary>
internal enum ShipmentStatusContract
{
    /// <summary>Being prepared.</summary>
    Preparing,

    /// <summary>With the carrier.</summary>
    InTransit,

    /// <summary>Delivered.</summary>
    Delivered,

    /// <summary>Delivery failed or was returned.</summary>
    Failed,

    /// <summary>Cancelled because its order was cancelled before the carrier took it.</summary>
    Cancelled,
}

/// <summary>Shipment resource.</summary>
/// <param name="Id">Shipment identifier.</param>
/// <param name="OrderId">Order being fulfilled.</param>
/// <param name="Status">Shipment status.</param>
/// <param name="Carrier">Carrier name, omitted until one is assigned.</param>
/// <param name="TrackingCode">Carrier tracking code, omitted until available.</param>
/// <param name="EstimatedDeliveryDate">Estimated delivery as a calendar date (<c>YYYY-MM-DD</c>), omitted until available.</param>
internal sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    ShipmentStatusContract Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Carrier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TrackingCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? EstimatedDeliveryDate
);
