using Microsoft.EntityFrameworkCore;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Catalog.Infrastructure;

/// <summary>
/// The Catalog context's database (ai/DATABASE.md §5): schema <c>catalog</c>, its own migration history and
/// outbox/inbox tables. It maps only Catalog entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "catalog";

    /// <summary>The product catalog.</summary>
    public DbSet<Product> Products => Set<Product>();
}
