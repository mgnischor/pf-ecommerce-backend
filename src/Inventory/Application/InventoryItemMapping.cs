using Portfolio.Inventory.Domain;

namespace Portfolio.Inventory.Application;

/// <summary>Projects the aggregate onto its read model.</summary>
internal static class InventoryItemMapping
{
    /// <summary>Builds the view of <paramref name="item"/>.</summary>
    /// <param name="item">Inventory item.</param>
    public static InventoryItemView ToView(this InventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryItemView(item.Sku.Value, item.OnHand, item.Reserved, item.Available, item.Version);
    }
}
