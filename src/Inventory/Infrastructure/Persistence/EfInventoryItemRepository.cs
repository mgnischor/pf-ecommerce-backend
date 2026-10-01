using Microsoft.EntityFrameworkCore;
using Portfolio.Inventory.Domain;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IInventoryItemRepository"/>. The soft-delete filter of the entity conventions keeps
/// deleted items out of every query; commits go through <see cref="InventoryDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfInventoryItemRepository(InventoryDbContext context) : IInventoryItemRepository
{
    /// <inheritdoc />
    public Task<InventoryItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.InventoryItems.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<InventoryItem?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);
        return context.InventoryItems.FirstOrDefaultAsync(item => item.Sku == sku, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockMovement?> FindMovementAsync(
        Guid itemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    ) =>
        context
            .StockMovements.AsNoTracking()
            .FirstOrDefaultAsync(
                movement => movement.InventoryItemId == itemId && movement.IdempotencyKey == idempotencyKey,
                cancellationToken
            );

    /// <inheritdoc />
    public async Task AddAsync(InventoryItem aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.InventoryItems.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(InventoryItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // Aggregates are loaded through the repository, so they are tracked and need no staging. Attaching a detached
        // one would carry its current version as the "original" and bypass the optimistic-concurrency check.
        if (context.Entry(aggregate).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Only an aggregate loaded through the repository can be updated.");
        }
    }

    /// <inheritdoc />
    public void Remove(InventoryItem aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.InventoryItems.Remove(aggregate);
    }
}
