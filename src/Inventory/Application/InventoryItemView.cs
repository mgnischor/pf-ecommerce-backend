namespace Portfolio.Inventory.Application;

/// <summary>Read model of an inventory item: what the API may show, without exposing the aggregate.</summary>
/// <param name="Sku">Normalized stock-keeping unit.</param>
/// <param name="OnHand">Physical units in stock.</param>
/// <param name="Reserved">Units held by open reservations.</param>
/// <param name="Available">Units that can still be sold (BR-INV-004).</param>
/// <param name="Version">Aggregate version, the source of the <c>ETag</c>.</param>
internal sealed record InventoryItemView(string Sku, int OnHand, int Reserved, int Available, int Version);
