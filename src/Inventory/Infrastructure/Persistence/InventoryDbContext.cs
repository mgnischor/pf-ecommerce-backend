using Microsoft.EntityFrameworkCore;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>
/// The Inventory context's database (ai/DATABASE.md §5): schema <c>inventory</c>, its own migration history and
/// outbox/inbox tables. It maps only Inventory entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "inventory";

    /// <summary>Inventory items, one per SKU.</summary>
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    /// <summary>The append-only stock ledger.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
}
