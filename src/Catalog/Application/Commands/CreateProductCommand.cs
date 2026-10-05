namespace Portfolio.Catalog.Application;

/// <summary>Request to add a product to the catalog in <c>Draft</c> status.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Sku">Stock-keeping unit, unique among active products.</param>
/// <param name="Price">Sell price amount.</param>
/// <param name="Currency">ISO 4217 currency code of the price.</param>
/// <param name="Description">Optional description.</param>
/// <param name="IdempotencyKey">Client key: replaying it returns the original product and never creates a second one (BR-CAT-008).</param>
internal sealed record CreateProductCommand(
    string? Name,
    string? Sku,
    decimal Price,
    string? Currency,
    string? Description = null,
    string? IdempotencyKey = null
);
