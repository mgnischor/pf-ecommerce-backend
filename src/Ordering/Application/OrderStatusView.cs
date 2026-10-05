namespace Portfolio.Ordering.Application;

/// <summary>Lifecycle status of an order as the API may show it (BR-ORD-003).</summary>
internal enum OrderStatusView
{
    /// <summary>Placed and waiting for payment.</summary>
    AwaitingPayment = 0,

    /// <summary>Paid; fulfillment can start.</summary>
    Paid = 1,

    /// <summary>Handed to the carrier.</summary>
    Shipped = 2,

    /// <summary>Delivered to the customer.</summary>
    Delivered = 3,

    /// <summary>Cancelled before it was shipped.</summary>
    Cancelled = 4,
}
