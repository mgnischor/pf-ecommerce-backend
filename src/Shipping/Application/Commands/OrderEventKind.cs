namespace Portfolio.Shipping.Application;

/// <summary>The Ordering integration events the Shipping context reacts to (BR-SHP-001, BR-SHP-004, BR-SHP-005).</summary>
internal enum OrderEventKind
{
    /// <summary><c>ordering.order-placed</c>: remember whose the order is.</summary>
    Placed = 0,

    /// <summary><c>ordering.order-paid</c>: start preparing the shipment.</summary>
    Paid = 1,

    /// <summary><c>ordering.order-cancelled</c>: cancel the shipment that is still being prepared.</summary>
    Cancelled = 2,
}
