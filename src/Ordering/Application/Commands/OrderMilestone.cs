namespace Portfolio.Ordering.Application;

/// <summary>The steps of an order's life that other contexts report to Ordering (BR-ORD-003).</summary>
internal enum OrderMilestone
{
    /// <summary>The payment was captured.</summary>
    Paid = 0,

    /// <summary>The order was handed to the carrier.</summary>
    Shipped = 1,

    /// <summary>The order was delivered.</summary>
    Delivered = 2,
}
