namespace Portfolio.Ordering.Domain;

/// <summary>
/// Lifecycle status of an <see cref="Order"/> (BR-ORD-003).
/// Valid transitions: AwaitingPayment → Paid → Shipped → Delivered, and AwaitingPayment | Paid → Cancelled.
/// </summary>
internal enum OrderStatus
{
    /// <summary>Placed and waiting for payment.</summary>
    AwaitingPayment = 0,

    /// <summary>Paid; fulfillment can start.</summary>
    Paid = 1,

    /// <summary>Handed to the carrier.</summary>
    Shipped = 2,

    /// <summary>Delivered to the customer. Terminal.</summary>
    Delivered = 3,

    /// <summary>Cancelled before it was shipped. Terminal.</summary>
    Cancelled = 4,
}
