namespace Portfolio.Catalog.Application;

/// <summary>Request to change the sell price of a product.</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Price">New price amount.</param>
/// <param name="Currency">ISO 4217 currency code of the price.</param>
internal sealed record ChangeProductPriceCommand(Guid ProductId, decimal Price, string? Currency);
