using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.IntegrationTests.Http;
using Portfolio.Inventory.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The roles of <c>database/provision-roles.sql</c> against a real server (ai/DATABASE.md §3.2): the migrator creates the
/// schema objects, the API runs on a role that can read and write them but never change them, and reporting cannot write.
/// The same migrations that production applies are applied here by the migrator role, on an empty database.
/// </summary>
public sealed class LeastPrivilegeTests : IDisposable
{
    private const string MigratorPassword = "migrator-password-for-tests-only";
    private const string RuntimePassword = "runtime-password-for-tests-only";
    private const string ReadonlyPassword = "readonly-password-for-tests-only";

    private const string InsufficientPrivilege = "42501";
    private const string ReadOnlyTransaction = "25006";

    private readonly TestDatabase _database = PostgresFixture.Current.CreateEmptyDatabase();

    public void Dispose() => _database.Dispose();

    private string ConnectionFor(string role, string password) =>
        new NpgsqlConnectionStringBuilder(_database.ConnectionString)
        {
            Username = role,
            Password = password,
        }.ConnectionString;

    private static string ScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Portfolio.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "database", "provision-roles.sql");
    }

    /// <summary>Runs the provisioning script as the bootstrap superuser, passing the passwords as session settings.</summary>
    private async Task ProvisionAsync()
    {
        await using var dataSource = _database.OpenDataSource();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        foreach (
            var (setting, value) in new[]
            {
                ("pf.migrator_password", MigratorPassword),
                ("pf.runtime_password", RuntimePassword),
                ("pf.readonly_password", ReadonlyPassword),
            }
        )
        {
            await using var set = new NpgsqlCommand("SELECT set_config($1, $2, false)", connection)
            {
                Parameters =
                {
                    new NpgsqlParameter { Value = setting },
                    new NpgsqlParameter { Value = value },
                },
            };
            await set.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var script = new NpgsqlCommand(
            await File.ReadAllTextAsync(ScriptPath(), TestContext.Current.CancellationToken),
            connection
        );
        await script.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The deployment order: provision, migrate as the migrator, provision again to grant on what was created.</summary>
    private async Task DeployAsync()
    {
        await ProvisionAsync();

        await using var migrator = new NpgsqlDataSourceBuilder(ConnectionFor("app_migrator", MigratorPassword)).Build();
        await using (var identity = TestContexts.Identity(migrator))
        {
            await identity.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (var catalog = TestContexts.Catalog(migrator))
        {
            await catalog.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using (var inventory = TestContexts.Inventory(migrator))
        {
            await inventory.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await ProvisionAsync();
    }

    private async Task<PostgresException> RefusedAsync(string role, string password, string sql)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(ConnectionFor(role, password)).Build();
        await using var command = dataSource.CreateCommand(sql);

        return await Should.ThrowAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Should_let_the_migrator_create_every_schema_object_the_migrations_define()
    {
        await DeployAsync();

        var owners = await _database.StringsAsync(
            "SELECT DISTINCT tableowner FROM pg_tables WHERE schemaname IN ('identity', 'catalog', 'inventory')"
        );
        var schemaOwners = await _database.StringsAsync(
            "SELECT DISTINCT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname IN ('identity', 'catalog', 'inventory')"
        );

        owners.ShouldBe(["app_migrator"]);
        schemaOwners.ShouldBe(["app_migrator"]);
    }

    [Fact]
    public async Task Should_let_the_runtime_role_read_insert_and_update_through_the_real_contexts()
    {
        await DeployAsync();
        var clock = TestContexts.NewClock();
        await using var runtime = new NpgsqlDataSourceBuilder(ConnectionFor("app_runtime", RuntimePassword)).Build();

        await using (var writer = TestContexts.Inventory(runtime, clock))
        {
            var item = InventoryItem.Open(Sku.Create("CAF-600-PRT").Value, clock);
            item.Adjust(10, "stocktake", Guid.CreateVersion7(), "key-0001-aaaa", clock);
            writer.InventoryItems.Add(item);
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = TestContexts.Inventory(runtime, clock);
        var loaded = await reader.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);
        loaded.Adjust(-2, "damage", Guid.CreateVersion7(), "key-0002-bbbb", clock).IsSuccess.ShouldBeTrue();
        await reader.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _database.ScalarAsync("SELECT on_hand FROM inventory.inventory_items")).ShouldBe(8);
        (await _database.ScalarAsync("SELECT count(*) FROM inventory.outbox_messages")).ShouldBe(3L);
    }

    [Theory]
    [InlineData("DELETE FROM inventory.inventory_items")]
    [InlineData("TRUNCATE inventory.inventory_items")]
    [InlineData("CREATE TABLE inventory.rogue (id int)")]
    [InlineData("CREATE TABLE public.rogue (id int)")]
    [InlineData("ALTER TABLE inventory.inventory_items ADD COLUMN rogue int")]
    [InlineData("DROP TABLE inventory.inventory_items")]
    [InlineData("DROP INDEX inventory.ux_inventory_items_sku_active")]
    [InlineData("SELECT migration_id FROM inventory.__ef_migrations_history")]
    [InlineData("DELETE FROM identity.__ef_migrations_history")]
    [InlineData("CREATE SCHEMA rogue")]
    [InlineData("CREATE ROLE rogue LOGIN")]
    public async Task Should_refuse_the_runtime_role_anything_that_changes_the_schema_or_the_roles(string sql)
    {
        await DeployAsync();

        var refusal = await RefusedAsync("app_runtime", RuntimePassword, sql);

        refusal.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task Should_give_each_role_only_the_rights_of_its_job()
    {
        await DeployAsync();

        var roles = await _database.StringsAsync(
            """
            SELECT rolname || ':' || (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication)::text
            FROM pg_roles WHERE rolname LIKE 'app\_%' ORDER BY rolname
            """
        );

        roles.ShouldBe(["app_migrator:false", "app_readonly:false", "app_runtime:false"]);
    }

    [Fact]
    public async Task Should_let_the_reporting_role_read_but_never_write()
    {
        await DeployAsync();
        await using var readonlySource = new NpgsqlDataSourceBuilder(
            ConnectionFor("app_readonly", ReadonlyPassword)
        ).Build();
        await using (var read = readonlySource.CreateCommand("SELECT count(*) FROM catalog.products"))
        {
            (await read.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(0L);
        }

        var refusal = await RefusedAsync(
            "app_readonly",
            ReadonlyPassword,
            $"INSERT INTO inventory.outbox_messages (id, type, payload, aggregate_id, aggregate_version, occurred_at, attempts) VALUES ('{Guid.CreateVersion7()}', 'T', '[]', '{Guid.CreateVersion7()}', 1, now(), 0)"
        );

        refusal.SqlState.ShouldBeOneOf(InsufficientPrivilege, ReadOnlyTransaction);
    }

    [Fact]
    public async Task Should_be_repeatable_and_rotate_the_passwords_when_run_again()
    {
        await DeployAsync();

        await ProvisionAsync();

        await using var runtime = new NpgsqlDataSourceBuilder(ConnectionFor("app_runtime", RuntimePassword)).Build();
        await using var command = runtime.CreateCommand("SELECT 1");
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Should_refuse_to_provision_without_a_strong_password_instead_of_creating_a_weak_role()
    {
        await using var dataSource = _database.OpenDataSource();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var script = new NpgsqlCommand(
            await File.ReadAllTextAsync(ScriptPath(), TestContext.Current.CancellationToken),
            connection
        );

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            script.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)
        );

        failure.MessageText.ShouldContain("pf.");
    }

    [Fact]
    public async Task Should_run_the_whole_api_on_the_runtime_role_without_any_ddl_right()
    {
        await DeployAsync();
        using var factory = new ApiFactory(
            "Production",
            database: _database,
            connectionString: ConnectionFor("app_runtime", RuntimePassword)
        );
        using var client = factory.CreateClient();
        var staff = factory.Accounts["manager"];

        // Start-up created the bootstrap accounts, and sign-in, refresh and writes all work as app_runtime.
        var tokens = await AuthClient.SignInAsync(client, staff);
        using var refreshed = await AuthClient.RefreshAsync(client, tokens.RefreshToken);
        using var opened = await AuthClient.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            tokens.AccessToken,
            new { sku = "LEAST-PRIV-1" }
        );
        using var ready = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        opened.StatusCode.ShouldBe(HttpStatusCode.Created);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _database.ScalarAsync("SELECT count(*) FROM identity.users")).ShouldBe(5L);
    }
}
