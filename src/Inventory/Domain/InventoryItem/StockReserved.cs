using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>Event raised when units are held for an order in progress (BR-INV-005).</summary>
internal sealed record StockReserved(
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
    /// <summary>Creates the event for a reservation. Must be called after the state change.</summary>
    /// <param name="item">The item that was reserved from.</param>
    /// <param name="quantity">Units reserved.</param>
    public static StockReserved For(InventoryItem item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new StockReserved(
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
