using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Portfolio.Catalog.Infrastructure;
using Portfolio.Customers.Infrastructure;
using Portfolio.Identity.Infrastructure;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// Builds the context of each bounded context exactly as the application does (same provider options, naming
/// convention, and interceptors), but on a data source and a clock the test controls.
/// </summary>
internal static class TestContexts
{
    public static IdentityDbContext Identity(
        NpgsqlDataSource dataSource,
        TimeProvider? clock = null,
        IEnumerable<IDomainEventSubscriber>? subscribers = null
    ) => new(Options<IdentityDbContext>(IdentityDbContext.SchemaName, dataSource, clock, subscribers));

    public static CatalogDbContext Catalog(
        NpgsqlDataSource dataSource,
        TimeProvider? clock = null,
        IEnumerable<IDomainEventSubscriber>? subscribers = null
    ) => new(Options<CatalogDbContext>(CatalogDbContext.SchemaName, dataSource, clock, subscribers));

    public static InventoryDbContext Inventory(
        NpgsqlDataSource dataSource,
        TimeProvider? clock = null,
        IEnumerable<IDomainEventSubscriber>? subscribers = null
    ) => new(Options<InventoryDbContext>(InventoryDbContext.SchemaName, dataSource, clock, subscribers));

    public static CustomersDbContext Customers(
        NpgsqlDataSource dataSource,
        TimeProvider? clock = null,
        IEnumerable<IDomainEventSubscriber>? subscribers = null
    ) => new(Options<CustomersDbContext>(CustomersDbContext.SchemaName, dataSource, clock, subscribers));

    public static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private static DbContextOptions<TContext> Options<TContext>(
        string schema,
        NpgsqlDataSource dataSource,
        TimeProvider? clock,
        IEnumerable<IDomainEventSubscriber>? subscribers
    )
        where TContext : ModuleDbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        PostgresContextOptions.Configure(builder, schema, dataSource, new DatabaseOptions { MaxRetryCount = 0 });
        builder.AddInterceptors(
            new AuditingSaveChangesInterceptor(clock ?? TimeProvider.System),
            new OutboxSaveChangesInterceptor(subscribers ?? [], NullLogger<OutboxSaveChangesInterceptor>.Instance)
        );
        return builder.Options;
    }
}
