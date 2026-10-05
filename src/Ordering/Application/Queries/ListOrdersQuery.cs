namespace Portfolio.Ordering.Application;

/// <summary>Request to list the caller's orders (BR-ORD-006).</summary>
/// <param name="CustomerId">The authenticated account; only their own orders are ever listed.</param>
/// <param name="Limit">Page size; clamped to 1–100.</param>
/// <param name="Cursor">Signed cursor of the previous page, or <c>null</c> for the first page.</param>
/// <param name="Sort">One of <c>placedAt</c> or <c>total</c>, optionally prefixed with <c>-</c>; <c>null</c> means <c>-placedAt</c>.</param>
/// <param name="Status">Only orders in this status, or <c>null</c>.</param>
/// <param name="CreatedFrom">Only orders placed on or after this calendar date (UTC), or <c>null</c>.</param>
internal sealed record ListOrdersQuery(
    Guid CustomerId,
    int Limit,
    string? Cursor,
    string? Sort,
    OrderStatusView? Status,
    DateOnly? CreatedFrom
);
