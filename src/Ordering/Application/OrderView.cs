namespace Portfolio.Ordering.Application;

/// <summary>Read model of one order with its price snapshot.</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Number">Human-readable order number.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Items">Lines of the order.</param>
/// <param name="TotalAmount">Order total amount, captured at purchase time.</param>
/// <param name="Currency">ISO 4217 currency of the total.</param>
/// <param name="PlacedAt">UTC instant the order was placed.</param>
/// <param name="Version">Aggregate version, the source of the <c>ETag</c>.</param>
/// <param name="AllowedActions">Actions the state machine currently allows (see <see cref="OrderActions"/>).</param>
internal sealed record OrderView(
    Guid Id,
    string Number,
    OrderStatusView Status,
    IReadOnlyList<OrderItemView> Items,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset PlacedAt,
    int Version,
    IReadOnlyList<string> AllowedActions
);
