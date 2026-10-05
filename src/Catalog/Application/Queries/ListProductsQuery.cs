namespace Portfolio.Catalog.Application;

/// <summary>Request to list products (BR-CAT-006).</summary>
/// <param name="Limit">Page size; clamped to 1–100.</param>
/// <param name="Cursor">Signed cursor of the previous page, or <c>null</c> for the first page.</param>
/// <param name="Search">Text matched against the name (substring) and the SKU (exact), or <c>null</c>.</param>
/// <param name="Sort">One of <c>name</c>, <c>price</c>, <c>createdAt</c>, optionally prefixed with <c>-</c>; <c>null</c> means <c>-createdAt</c>.</param>
/// <param name="Status">Only products in this status, or <c>null</c> for every status the caller may see.</param>
/// <param name="CanSeeAllStatuses">Whether the caller is catalog staff; everyone else sees only active products.</param>
internal sealed record ListProductsQuery(
    int Limit,
    string? Cursor,
    string? Search,
    string? Sort,
    ProductStatusView? Status,
    bool CanSeeAllStatuses
);
