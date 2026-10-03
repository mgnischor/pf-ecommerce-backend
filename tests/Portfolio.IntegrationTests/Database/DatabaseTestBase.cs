using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Portfolio.Catalog.Domain;
using Portfolio.Catalog.Infrastructure;
using Portfolio.Customers.Infrastructure;
using Portfolio.Identity.Infrastructure;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// A test that owns a PostgreSQL database cloned from the migrated template, a data source on it, and a clock it
/// controls. Contexts are built as the application builds them and disposed with the test.
/// </summary>
public abstract class DatabaseTestBase : IDisposable
{
    private readonly List<IDisposable> _contexts = [];

    protected DatabaseTestBase()
    {
        Database = PostgresFixture.Current.CreateDatabase();
        DataSource = Database.OpenDataSource();
    }

    internal TestDatabase Database { get; }

    internal NpgsqlDataSource DataSource { get; }

    internal FakeTimeProvider Clock { get; } = TestContexts.NewClock();

    internal IdentityDbContext Identity() => Track(TestContexts.Identity(DataSource, Clock));

    internal CatalogDbContext Catalog() => Track(TestContexts.Catalog(DataSource, Clock));

    internal InventoryDbContext Inventory() => Track(TestContexts.Inventory(DataSource, Clock));

    internal CustomersDbContext Customers() => Track(TestContexts.Customers(DataSource, Clock));

    internal Product NewProduct(string sku = "CAF-600-PRT", decimal price = 189.90m) =>
        Product
            .Create(
                "Cafeteira Elétrica 600ml",
                Sku.Create(sku).Value,
                new Money(price, "BRL"),
                "Filtro permanente.",
                Clock
            )
            .Value;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        DataSource.Dispose();
        Database.Dispose();
    }

    private T Track<T>(T context)
        where T : IDisposable
    {
        _contexts.Add(context);
        return context;
    }
}
