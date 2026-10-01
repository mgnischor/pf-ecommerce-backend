namespace Portfolio.Inventory.Application;

/// <summary>Request to start tracking the stock of a SKU, with no units on hand.</summary>
/// <param name="Sku">Stock-keeping unit, unique among active items.</param>
internal sealed record OpenInventoryItemCommand(string? Sku);
