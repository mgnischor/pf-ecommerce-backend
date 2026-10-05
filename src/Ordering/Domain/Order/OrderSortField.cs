namespace Portfolio.Ordering.Domain;

/// <summary>Field an order listing can be ordered by (BR-ORD-006).</summary>
internal enum OrderSortField
{
    /// <summary>The instant the order was placed; the default, newest first.</summary>
    PlacedAt = 0,

    /// <summary>The order total amount.</summary>
    Total = 1,
}
