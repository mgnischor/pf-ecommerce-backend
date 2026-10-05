namespace Portfolio.Ordering.Application;

/// <summary>Read model of an order in a collection: what the API may show, without exposing the aggregate.</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Number">Human-readable order number.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="TotalAmount">Order total amount, captured at purchase time.</param>
/// <param name="Currency">ISO 4217 currency of the total.</param>
/// <param name="PlacedAt">UTC instant the order was placed.</param>
internal sealed record OrderSummaryView(
    Guid Id,
    string Number,
    OrderStatusView Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset PlacedAt
);
