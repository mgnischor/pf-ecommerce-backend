using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The migrations, applied to a real PostgreSQL (ai/DATABASE.md §8.2): each context owns exactly its schema, its own
/// history, and its own outbox and inbox; the model snapshot matches the model; and every migration can be undone.
/// </summary>
public sealed class MigrationTests : DatabaseTestBase
{
    private static readonly string[] Technical = ["__ef_migrations_history", "inbox_messages", "outbox_messages"];

    [Theory]
    [InlineData("identity", "refresh_tokens,users")]
    [InlineData("catalog", "products")]
    [InlineData("inventory", "inventory_items,stock_movements")]
    [InlineData("customers", "customer_profiles")]
    [InlineData("cart", "carts,cart_items,catalog_products")]
    [InlineData("ordering", "orders,order_items")]
    [InlineData("shipping", "shipments,order_references")]
    public async Task Should_create_in_each_schema_its_own_tables_and_its_own_outbox_inbox_and_history(
        string schema,
        string domainTables
    )
    {
        var tables = await Database.StringsAsync(
            $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{schema}' ORDER BY table_name"
        );

        tables.ShouldBe([.. Technical.Concat(domainTables.Split(',')).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Should_create_nothing_outside_the_four_context_schemas()
    {
        var schemas = await Database.StringsAsync(
            """
            SELECT DISTINCT table_schema FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema
            """
        );

        schemas.ShouldBe(["cart", "catalog", "customers", "identity", "inventory", "ordering", "shipping"]);
    }

    [Fact]
    public async Task Should_keep_the_applied_migrations_in_each_contexts_own_history_table_in_order()
    {
        foreach (
            var schema in new[] { "identity", "catalog", "inventory", "customers", "cart", "ordering", "shipping" }
        )
        {
            var applied = await Database.StringsAsync(
                $"SELECT migration_id FROM {schema}.__ef_migrations_history ORDER BY migration_id"
            );

            // The three older contexts added the trace columns in a second migration; Customers was born with them.
            // Catalog then added the idempotency keys of its creation and deletion (BR-CAT-008) in a third.
            var expected = schema switch
            {
                "customers" => 1,
                "cart" => 1,
                "ordering" => 1,
                "shipping" => 1,
                "catalog" => 3,
                _ => 2,
            };

            applied.Count.ShouldBe(expected);
            applied[0].ShouldEndWith($"_Create{char.ToUpperInvariant(schema[0])}{schema[1..]}Schema");
            if (expected >= 2)
            {
                applied[1].ShouldEndWith("_AddOutboxTraceContext");
            }

            if (expected == 3)
            {
                applied[2].ShouldEndWith("_AddProductIdempotencyKeys");
            }
        }
    }

    [Fact]
    public async Task Should_add_the_trace_context_columns_to_every_outbox_as_nullable_so_the_old_version_keeps_working()
    {
        foreach (
            var schema in new[] { "identity", "catalog", "inventory", "customers", "cart", "ordering", "shipping" }
        )
        {
            var columns = await Database.StringsAsync(
                $"""
                SELECT column_name || ':' || is_nullable FROM information_schema.columns
                WHERE table_schema = '{schema}' AND table_name = 'outbox_messages'
                  AND column_name IN ('trace_parent', 'trace_state') ORDER BY column_name
                """
            );

            // Expand phase of expand/contract: an application that does not know the columns still inserts rows.
            columns.ShouldBe(["trace_parent:YES", "trace_state:YES"]);
        }
    }

    [Fact]
    public async Task Should_not_have_a_foreign_key_that_crosses_a_schema()
    {
        var crossing = await Database.StringsAsync(
            """
            SELECT c.conname
            FROM pg_constraint c
            JOIN pg_class child ON child.oid = c.conrelid
            JOIN pg_class parent ON parent.oid = c.confrelid
            WHERE c.contype = 'f' AND child.relnamespace <> parent.relnamespace
            """
        );

        crossing.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("identity", "users", true)]
    [InlineData("identity", "refresh_tokens", true)]
    [InlineData("catalog", "products", true)]
    [InlineData("inventory", "inventory_items", true)]
    [InlineData("customers", "customer_profiles", true)]
    [InlineData("cart", "carts", true)]
    [InlineData("cart", "cart_items", false)]
    [InlineData("cart", "catalog_products", true)]
    [InlineData("ordering", "orders", true)]
    [InlineData("ordering", "order_items", false)]
    [InlineData("shipping", "shipments", true)]
    [InlineData("shipping", "order_references", false)]
    [InlineData("inventory", "stock_movements", false)]
    public async Task Should_give_every_domain_table_the_traceability_columns_and_aggregate_roots_a_version(
        string schema,
        string table,
        bool isAggregateRoot
    )
    {
        var columns = await Database.StringsAsync(
            $"SELECT column_name FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}'"
        );

        columns.ShouldContain("id");
        columns.ShouldContain("created_at");
        columns.ShouldContain("updated_at");
        columns.ShouldContain("deleted_at");
        columns.Contains("version").ShouldBe(isAggregateRoot);

        var types = await Database.StringsAsync(
            $"""
            SELECT data_type FROM information_schema.columns
            WHERE table_schema = '{schema}' AND table_name = '{table}'
              AND column_name IN ('created_at', 'updated_at', 'deleted_at')
            """
        );
        types.ShouldAllBe(type => type == "timestamp with time zone");
    }

    [Theory]
    [InlineData("catalog", "products", "price_amount", "numeric", 19, 4)]
    public async Task Should_store_money_as_numeric_19_4_never_floating_point(
        string schema,
        string table,
        string column,
        string type,
        int precision,
        int scale
    )
    {
        var definition = await Database.StringsAsync(
            $"""
            SELECT data_type || ',' || numeric_precision || ',' || numeric_scale
            FROM information_schema.columns
            WHERE table_schema = '{schema}' AND table_name = '{table}' AND column_name = '{column}'
            """
        );

        definition.ShouldHaveSingleItem().ShouldBe($"{type},{precision},{scale}");
    }

    [Fact]
    public void Should_have_no_pending_model_changes_so_the_snapshot_matches_the_model()
    {
        Identity().Database.HasPendingModelChanges().ShouldBeFalse();
        Catalog().Database.HasPendingModelChanges().ShouldBeFalse();
        Inventory().Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Should_apply_again_without_changing_anything()
    {
        var context = Inventory();
        var before = await Database.StringsAsync("SELECT migration_id FROM inventory.__ef_migrations_history");

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        (await Database.StringsAsync("SELECT migration_id FROM inventory.__ef_migrations_history")).ShouldBe(before);
    }

    [Theory]
    [InlineData("identity", 4L)]
    [InlineData("catalog", 3L)]
    [InlineData("inventory", 4L)]
    public async Task Should_undo_every_migration_and_apply_it_again(string schema, long tables)
    {
        ModuleDbContext context = schema switch
        {
            "identity" => Identity(),
            "catalog" => Catalog(),
            _ => Inventory(),
        };
        var migrator = context.GetService<IMigrator>();
        var tablesSql =
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name <> '__ef_migrations_history'";

        await migrator.MigrateAsync("0", TestContext.Current.CancellationToken);
        var afterDown = await Database.ScalarAsync(tablesSql);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var afterUp = await Database.ScalarAsync(tablesSql);

        afterDown.ShouldBe(0L);
        afterUp.ShouldBe(tables);
    }
}
