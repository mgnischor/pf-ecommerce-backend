using Microsoft.EntityFrameworkCore;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>
/// The Shipping context's database (ai/DATABASE.md §5): schema <c>shipping</c>, its own migration history and
/// outbox/inbox tables. It maps only Shipping entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "shipping";

    /// <summary>Shipments.</summary>
    public DbSet<Shipment> Shipments => Set<Shipment>();

    /// <summary>The context's own record of whose each order is.</summary>
    public DbSet<OrderReference> OrderReferences => Set<OrderReference>();
}
