using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// Event raised for every manual stock movement (BR-INV-003). Carries the resulting levels so consumers
/// (catalog availability, alerts) never need to query back.
/// </summary>
internal sealed record StockAdjusted(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Sku,
    int Delta,
    string ReasonCode,
    int OnHand,
    int Reserved
) : IDomainEvent
{
    /// <summary>Creates the event for an adjustment. Must be called after the state change.</summary>
    /// <param name="item">The adjusted item.</param>
    /// <param name="movement">The ledger movement that was appended.</param>
    public static StockAdjusted For(InventoryItem item, StockMovement movement)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(movement);

        return new StockAdjusted(
            Guid.CreateVersion7(item.UpdatedAt),
            item.Id,
            item.Version,
            item.UpdatedAt,
            item.Sku.Value,
            movement.Delta,
            movement.ReasonCode,
            item.OnHand,
            item.Reserved
        );
    }
}
