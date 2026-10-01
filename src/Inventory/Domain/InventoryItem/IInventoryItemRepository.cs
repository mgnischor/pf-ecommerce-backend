using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="InventoryItem"/> aggregate.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IInventoryItemRepository : IRepository<InventoryItem>
{
    /// <summary>Finds an active (not deleted) item by SKU, or <c>null</c> when not found.</summary>
    /// <param name="sku">Stock-keeping unit to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InventoryItem?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the ledger movement a client already recorded with <paramref name="idempotencyKey"/> for an item
    /// (BR-INV-003), or <c>null</c> when the key was never used on it.
    /// </summary>
    /// <param name="itemId">Inventory item identifier.</param>
    /// <param name="idempotencyKey">Key sent by the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StockMovement?> FindMovementAsync(
        Guid itemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    );
}
