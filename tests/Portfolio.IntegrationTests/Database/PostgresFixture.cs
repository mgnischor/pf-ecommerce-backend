using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// One disposable PostgreSQL for the whole test run (ai/TESTS.md §4.1, ai/DATABASE.md §8.3): never the EF Core
/// in-memory or SQLite providers. Every migration of every context is applied once to a template database; each
/// test then gets its own database cloned from it, so tests share no state and need no clean-up between them.
/// The image is the one Compose runs, pinned by digest, so tests exercise the production engine version.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image =
        "postgres:18.6-alpine3.24@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873";

    private const string TemplateName = "pf_template";

    private PostgreSqlContainer _container = default!;
    private string _adminConnection = default!;

    /// <summary>The fixture of the running test assembly.</summary>
    internal static PostgresFixture Current { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder(Image).WithTmpfsMount("/var/lib/postgresql").Build();
        await _container.StartAsync();

        var admin = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false };
        _adminConnection = admin.ConnectionString;

        await using (var connection = new NpgsqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {TemplateName}", connection);
            await create.ExecuteNonQueryAsync();
        }

        await MigrateTemplateAsync();
        Current = this;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await _container.DisposeAsync();
    }

    /// <summary>Creates an isolated database with every migration applied. Dispose it to drop it.</summary>
    internal TestDatabase CreateDatabase() => Create($"CREATE DATABASE {{0}} TEMPLATE {TemplateName}");

    /// <summary>Creates an empty database, for tests that run the provisioning and the migrations themselves.</summary>
    internal TestDatabase CreateEmptyDatabase() => Create("CREATE DATABASE {0}");

    private TestDatabase Create(string statement)
    {
        var name = $"pf_test_{Guid.NewGuid():N}";

        using (var connection = new NpgsqlConnection(_adminConnection))
        {
            connection.Open();
            // The name is generated here from a GUID: it is never input.
            using var create = new NpgsqlCommand(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, name),
                connection
            );
            create.ExecuteNonQuery();
        }

        var builder = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = name, Pooling = true };
        return new TestDatabase(name, builder.ConnectionString, _adminConnection);
    }

    private async Task MigrateTemplateAsync()
    {
        var template = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = TemplateName };
        await using var dataSource = new NpgsqlDataSourceBuilder(template.ConnectionString).Build();

        await using (var identity = TestContexts.Identity(dataSource))
        {
            await identity.Database.MigrateAsync();
        }

        await using (var catalog = TestContexts.Catalog(dataSource))
        {
            await catalog.Database.MigrateAsync();
        }

        await using (var inventory = TestContexts.Inventory(dataSource))
        {
            await inventory.Database.MigrateAsync();
        }

        await using (var customers = TestContexts.Customers(dataSource))
        {
            await customers.Database.MigrateAsync();
        }

        await using (var cart = TestContexts.Cart(dataSource))
        {
            await cart.Database.MigrateAsync();
        }

        await using (var ordering = TestContexts.Ordering(dataSource))
        {
            await ordering.Database.MigrateAsync();
        }

        await using (var shipping = TestContexts.Shipping(dataSource))
        {
            await shipping.Database.MigrateAsync();
        }

        // A template cannot be cloned while a connection to it is open.
        NpgsqlConnection.ClearAllPools();
    }
}
