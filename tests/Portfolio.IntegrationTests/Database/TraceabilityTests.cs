using Microsoft.EntityFrameworkCore;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// Traceability, versioning and soft delete as the database sees them (ai/DATABASE.md §2, §8.2): the timestamps come
/// from the injected clock, the version advances with every change, a deletion keeps the row, and a deleted row
/// releases its business key.
/// </summary>
public sealed class TraceabilityTests : DatabaseTestBase
{
    private async Task<(DateTimeOffset Created, DateTimeOffset Updated, DateTimeOffset? Deleted, int Version)> RowAsync(
        Guid id
    )
    {
        await using var command = DataSource.CreateCommand(
            "SELECT created_at, updated_at, deleted_at, version FROM catalog.products WHERE id = $1"
        );
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        return (
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetInt32(3)
        );
    }

    [Fact]
    public async Task Should_stamp_creation_and_start_the_version_at_one()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = await RowAsync(product.Id);
        row.Created.ShouldBe(TestContexts.NewClock().GetUtcNow());
        row.Updated.ShouldBe(row.Created);
        row.Deleted.ShouldBeNull();
        row.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Fact]
    public async Task Should_advance_the_update_timestamp_and_the_version_on_every_change()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Clock.Advance(TimeSpan.FromMinutes(5));
        product.ChangePrice(new Money(199.90m, "BRL"), Clock).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Clock.Advance(TimeSpan.FromMinutes(5));
        product.Activate(Clock).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = await RowAsync(product.Id);
        row.Version.ShouldBe(3);
        row.Created.ShouldBe(TestContexts.NewClock().GetUtcNow());
        row.Updated.ShouldBe(TestContexts.NewClock().GetUtcNow().AddMinutes(10));
    }

    [Fact]
    public async Task Should_keep_the_version_of_the_row_equal_to_the_version_of_the_aggregate()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        product.Activate(Clock);
        product.Discontinue(Clock);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await RowAsync(product.Id)).Version.ShouldBe(product.Version);
    }

    [Fact]
    public async Task Should_turn_a_removal_into_a_logical_deletion_that_keeps_the_row_and_its_history()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Clock.Advance(TimeSpan.FromHours(1));
        context.Products.Remove(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = await RowAsync(product.Id);
        row.Deleted.ShouldBe(TestContexts.NewClock().GetUtcNow().AddHours(1));
        row.Updated.ShouldBe(row.Deleted!.Value);
        row.Version.ShouldBe(2);
        (await Database.ScalarAsync("SELECT count(*) FROM catalog.products")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_hide_a_deleted_row_from_every_query_unless_an_audit_flow_asks_for_it()
    {
        var product = NewProduct();
        var writer = Catalog();
        writer.Products.Add(product);
        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        writer.Products.Remove(product);
        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reader = Catalog();

        (await reader.Products.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await reader.Products.FindAsync([product.Id], TestContext.Current.CancellationToken)).ShouldBeNull();
        (
            await reader
                .Products.IgnoreQueryFilters([EntityModelConventionsNames.SoftDelete])
                .CountAsync(TestContext.Current.CancellationToken)
        ).ShouldBe(1);
    }

    [Fact]
    public async Task Should_release_the_sku_of_a_deleted_product_and_refuse_a_second_active_one()
    {
        var first = NewProduct();
        var context = Catalog();
        context.Products.Add(first);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Products.Remove(first);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reuse = Catalog();
        reuse.Products.Add(NewProduct());
        await reuse.SaveChangesAsync(TestContext.Current.CancellationToken);

        var duplicate = Catalog();
        duplicate.Products.Add(NewProduct());
        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            duplicate.SaveChangesAsync(TestContext.Current.CancellationToken)
        );
        failure.Code.ShouldBe("DUPLICATE_RECORD");
        failure.Message.ShouldNotContain("CAF-600-PRT");
    }

    [Fact]
    public async Task Should_round_trip_money_without_losing_precision_or_the_currency()
    {
        var product = NewProduct(price: 12345.6789m);
        var writer = Catalog();
        writer.Products.Add(product);
        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loaded = await Catalog().Products.SingleAsync(TestContext.Current.CancellationToken);

        loaded.Price.ShouldBe(new Money(12345.6789m, "BRL"));
        (await Database.ScalarAsync("SELECT price_currency FROM catalog.products")).ShouldBe("BRL");
    }

    [Fact]
    public async Task Should_store_a_status_as_readable_text_and_read_it_back()
    {
        var product = NewProduct();
        product.Activate(Clock);
        var writer = Catalog();
        writer.Products.Add(product);
        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT status FROM catalog.products")).ShouldBe("Active");
        (await Catalog().Products.SingleAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(
            ProductStatus.Active
        );
    }

    // The filter is named so an audit flow can disable it without touching other filters.
    private static class EntityModelConventionsNames
    {
        public const string SoftDelete = Portfolio.SharedKernel.Infrastructure.EntityModelConventions.SoftDeleteFilter;
    }
}
