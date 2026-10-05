namespace Portfolio.Shipping.Domain;

/// <summary>
/// Lifecycle status of a <see cref="Shipment"/> (BR-SHP-002).
/// Valid transitions: Preparing → InTransit → Delivered | Failed, and Preparing → Cancelled.
/// </summary>
internal enum ShipmentStatus
{
    /// <summary>Being prepared for the carrier.</summary>
    Preparing = 0,

    /// <summary>With the carrier.</summary>
    InTransit = 1,

    /// <summary>Delivered to the customer. Terminal.</summary>
    Delivered = 2,

    /// <summary>Delivery failed or the shipment was returned. Terminal.</summary>
    Failed = 3,

    /// <summary>Cancelled before the carrier took it, because its order was cancelled. Terminal.</summary>
    Cancelled = 4,
}
