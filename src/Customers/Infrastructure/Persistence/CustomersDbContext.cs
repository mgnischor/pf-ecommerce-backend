using Microsoft.EntityFrameworkCore;
using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Customers.Infrastructure;

/// <summary>
/// The Customers context's database (ai/DATABASE.md §5): schema <c>customers</c>, its own migration history and
/// outbox/inbox tables. It maps only Customers entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "customers";

    /// <summary>Customer profiles, one per customer account.</summary>
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
}
