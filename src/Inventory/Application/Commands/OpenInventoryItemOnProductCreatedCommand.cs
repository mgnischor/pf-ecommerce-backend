namespace Portfolio.Inventory.Application;

/// <summary>
/// Reaction to the Catalog's <c>ProductCreated</c> integration event: start tracking the stock of the new product's SKU.
/// Inventory's own view of the event; it never references the Catalog's types.
/// </summary>
/// <param name="MessageId">Identifier of the event, which the inbox deduplicates on.</param>
/// <param name="Sku">SKU of the created product, as the Catalog published it.</param>
internal sealed record OpenInventoryItemOnProductCreatedCommand(Guid MessageId, string? Sku);
