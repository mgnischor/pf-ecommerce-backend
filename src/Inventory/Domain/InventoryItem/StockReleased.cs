using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>Event raised when held units go back to the available stock (BR-INV-006).</summary>
internal sealed record StockReleased(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Sku,
    int Quantity,
    int Reserved,
    int Available
) : IDomainEvent
{
    /// <summary>Creates the event for a release. Must be called after the state change.</summary>
    /// <param name="item">The item that was released to.</param>
    /// <param name="quantity">Units released.</param>
    public static StockReleased For(InventoryItem item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new StockReleased(
            Guid.CreateVersion7(item.UpdatedAt),
            item.Id,
            item.Version,
            item.UpdatedAt,
            item.Sku.Value,
            quantity,
            item.Reserved,
            item.Available
        );
    }
}
