using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.Inventory.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active items only" contract.</summary>
internal sealed class FakeInventoryItemRepository : IInventoryItemRepository
{
    private readonly List<InventoryItem> _items = [];

    public IReadOnlyList<InventoryItem> Items => _items;

    public int UpdateCalls { get; private set; }

    public Task<InventoryItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.FirstOrDefault(item => item.Id == id && !item.IsDeleted));

    public Task<InventoryItem?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.FirstOrDefault(item => item.Sku == sku && !item.IsDeleted));

    public Task<StockMovement?> FindMovementAsync(
        Guid itemId,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _items
                .FirstOrDefault(item => item.Id == itemId)
                ?.Movements.FirstOrDefault(movement =>
                    string.Equals(movement.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
                )
        );

    public Task AddAsync(InventoryItem aggregate, CancellationToken cancellationToken = default)
    {
        _items.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(InventoryItem aggregate) => UpdateCalls++;

    public void Remove(InventoryItem aggregate) => _items.Remove(aggregate);

    public void Seed(InventoryItem item) => _items.Add(item);
}

/// <summary>Counts commits so tests can assert that failed use cases persist nothing.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}
