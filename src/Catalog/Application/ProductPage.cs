namespace Portfolio.Catalog.Application;

/// <summary>One page of a product listing.</summary>
/// <param name="Items">Products of the page; empty, never <c>null</c>.</param>
/// <param name="NextCursor">Signed cursor of the next page, or <c>null</c> on the last page.</param>
/// <param name="HasMore">Whether another page exists.</param>
internal sealed record ProductPage(IReadOnlyList<ProductSummaryView> Items, string? NextCursor, bool HasMore);
