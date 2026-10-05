namespace Portfolio.Ordering.Domain;

/// <summary>What an order listing asks of the repository: only the customer's own orders, ever (BR-ORD-006).</summary>
/// <param name="CustomerId">Whose orders to list.</param>
/// <param name="SortField">Primary ordering; the identifier always breaks ties, in the same direction.</param>
/// <param name="Descending">Whether the order is descending.</param>
/// <param name="Limit">Maximum number of orders to return.</param>
/// <param name="Status">Only orders in this status, or <c>null</c> for every status.</param>
/// <param name="PlacedFrom">Only orders placed at or after this instant, or <c>null</c>.</param>
/// <param name="After">Continue after this position, or <c>null</c> for the first page.</param>
internal sealed record OrderListCriteria(
    Guid CustomerId,
    OrderSortField SortField,
    bool Descending,
    int Limit,
    OrderStatus? Status = null,
    DateTimeOffset? PlacedFrom = null,
    OrderSeekPosition? After = null
);
