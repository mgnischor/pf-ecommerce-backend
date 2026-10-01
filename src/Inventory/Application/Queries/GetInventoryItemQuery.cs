namespace Portfolio.Inventory.Application;

/// <summary>Request for the stock level of a SKU.</summary>
/// <param name="Sku">Stock-keeping unit, case-insensitive.</param>
internal sealed record GetInventoryItemQuery(string? Sku);
