using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>Event raised when an inventory item is opened for a SKU (BR-INV-008).</summary>
internal sealed record InventoryItemOpened(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Sku
) : IDomainEvent
{
    /// <summary>Creates the event for a newly opened item. Must be called after the state change.</summary>
    /// <param name="item">The opened item.</param>
    public static InventoryItemOpened For(InventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new InventoryItemOpened(
            Guid.CreateVersion7(item.CreatedAt),
            item.Id,
            item.Version,
            item.CreatedAt,
            item.Sku.Value
        );
    }
}
