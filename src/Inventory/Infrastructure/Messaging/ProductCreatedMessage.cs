namespace Portfolio.Inventory.Infrastructure;

/// <summary>Inventory's view of <c>ProductCreated</c>: only the fields it needs. Others in the payload are ignored.</summary>
/// <param name="Sku">SKU of the created product.</param>
internal sealed record ProductCreatedMessage(string? Sku);
