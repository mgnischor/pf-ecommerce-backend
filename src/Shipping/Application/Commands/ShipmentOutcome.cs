namespace Portfolio.Shipping.Application;

/// <summary>How a shipment in transit ended (BR-SHP-002).</summary>
internal enum ShipmentOutcome
{
    /// <summary>It reached the customer.</summary>
    Delivered = 0,

    /// <summary>Delivery failed or it was returned.</summary>
    Failed = 1,
}
