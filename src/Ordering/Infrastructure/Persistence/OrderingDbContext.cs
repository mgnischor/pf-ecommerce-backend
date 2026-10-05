using Microsoft.EntityFrameworkCore;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>
/// The Ordering context's database (ai/DATABASE.md §5): schema <c>ordering</c>, its own migration history and
/// outbox/inbox tables, and the sequence the order numbers come from (BR-ORD-005). It maps only Ordering entities and
/// never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "ordering";

    /// <summary>Name of the sequence order numbers are drawn from, in <see cref="SchemaName"/>.</summary>
    public const string OrderNumberSequence = "order_number_seq";

    /// <summary>Orders.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>The lines of the orders.</summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Numbers are drawn from the database so that concurrent placements never get the same one.
        modelBuilder.HasSequence<long>(OrderNumberSequence).StartsAt(1).IncrementsBy(1);
    }
}
