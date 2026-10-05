namespace Portfolio.Catalog.Domain;

/// <summary>What a product listing asks of the repository. Deleted products are never part of it.</summary>
/// <param name="SortField">Primary ordering; the identifier always breaks ties, in the same direction.</param>
/// <param name="Descending">Whether the order is descending.</param>
/// <param name="Limit">Maximum number of products to return.</param>
/// <param name="SearchText">Text matched against the name (substring) and the SKU (exact), or <c>null</c>.</param>
/// <param name="Status">Only products in this status, or <c>null</c> for every status.</param>
/// <param name="After">Continue after this position, or <c>null</c> for the first page.</param>
internal sealed record ProductListCriteria(
    ProductSortField SortField,
    bool Descending,
    int Limit,
    string? SearchText = null,
    ProductStatus? Status = null,
    ProductSeekPosition? After = null
);
