namespace Portfolio.Catalog.Application;

/// <summary>Request to read one product (BR-CAT-006).</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="CanSeeAllStatuses">Whether the caller is catalog staff; everyone else sees only active products.</param>
internal sealed record GetProductQuery(Guid ProductId, bool CanSeeAllStatuses);
