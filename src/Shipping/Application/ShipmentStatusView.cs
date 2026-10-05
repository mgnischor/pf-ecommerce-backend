namespace Portfolio.Shipping.Application;

/// <summary>Lifecycle status of a shipment as the API may show it (BR-SHP-002).</summary>
internal enum ShipmentStatusView
{
    /// <summary>Being prepared for the carrier.</summary>
    Preparing = 0,

    /// <summary>With the carrier.</summary>
    InTransit = 1,

    /// <summary>Delivered to the customer.</summary>
    Delivered = 2,

    /// <summary>Delivery failed or the shipment was returned.</summary>
    Failed = 3,

    /// <summary>Cancelled because its order was cancelled.</summary>
    Cancelled = 4,
}
