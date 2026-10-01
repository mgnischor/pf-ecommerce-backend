using Microsoft.EntityFrameworkCore;
using Portfolio.Identity.Domain;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// Optimistic concurrency on the aggregate <c>version</c> (ai/DATABASE.md §2.2, §8.2): of two writers that read the
/// same version, one commits and the other is told, never silently overwritten.
/// </summary>
public sealed class ConcurrencyTests : DatabaseTestBase
{
    private async Task<Guid> SeedProductAsync()
    {
        var product = NewProduct();
        var context = Catalog();
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return product.Id;
    }

    [Fact]
    public async Task Should_reject_the_second_writer_of_a_stale_aggregate_and_keep_the_first_write()
    {
        var id = await SeedProductAsync();
        var first = await Catalog().Products.SingleAsync(TestContext.Current.CancellationToken);
        var secondContext = Catalog();
        var second = await secondContext.Products.SingleAsync(TestContext.Current.CancellationToken);

        first.ShouldNotBeSameAs(second);
        using (var firstContext = TestContexts.Catalog(DataSource, Clock))
        {
            var tracked = await firstContext.Products.SingleAsync(TestContext.Current.CancellationToken);
            tracked.ChangePrice(new Money(100m, "BRL"), Clock);
            await firstContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        second.ChangePrice(new Money(200m, "BRL"), Clock);
        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            secondContext.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        failure.Code.ShouldBe("CONCURRENT_UPDATE");
        (await Database.ScalarAsync($"SELECT price_amount FROM catalog.products WHERE id = '{id}'")).ShouldBe(100m);
        (await Database.ScalarAsync($"SELECT version FROM catalog.products WHERE id = '{id}'")).ShouldBe(2);
    }

    [Fact]
    public async Task Should_let_two_writers_that_read_different_versions_both_commit_in_turn()
    {
        await SeedProductAsync();
        var context = Catalog();
        var product = await context.Products.SingleAsync(TestContext.Current.CancellationToken);

        product.ChangePrice(new Money(100m, "BRL"), Clock);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        product.ChangePrice(new Money(200m, "BRL"), Clock);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Database.ScalarAsync("SELECT version FROM catalog.products")).ShouldBe(3);
    }

    [Fact]
    public async Task Should_never_lose_an_update_when_many_requests_adjust_the_same_stock_at_once()
    {
        var item = InventoryItem.Open(Sku.Create("CAF-600-PRT").Value, Clock);
        var seed = Inventory();
        seed.InventoryItems.Add(item);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        const int writers = 12;
        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, writers)
                .Select(async index =>
                {
                    await using var context = TestContexts.Inventory(DataSource, Clock);
                    var current = await context.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);
                    current
                        .Adjust(1, "stocktake", Guid.CreateVersion7(), $"key-{index:D4}-0000", Clock)
                        .IsSuccess.ShouldBeTrue();

                    try
                    {
                        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                        return true;
                    }
                    catch (PersistenceConflictException)
                    {
                        return false;
                    }
                })
        );

        var committed = outcomes.Count(won => won);
        committed.ShouldBeGreaterThan(0);
        // Stock equals exactly the adjustments that committed: no unit lost, none counted twice.
        (await Database.ScalarAsync("SELECT on_hand FROM inventory.inventory_items")).ShouldBe(committed);
        (await Database.ScalarAsync("SELECT count(*) FROM inventory.stock_movements")).ShouldBe((long)committed);
        (await Database.ScalarAsync("SELECT version FROM inventory.inventory_items")).ShouldBe(1 + committed);
    }

    [Fact]
    public async Task Should_keep_stock_non_negative_even_when_two_writers_race_to_remove_the_last_units()
    {
        var item = InventoryItem.Open(Sku.Create("CAF-600-PRT").Value, Clock);
        item.Adjust(5, "stocktake", Guid.CreateVersion7(), "key-seed-0001", Clock);
        var seed = Inventory();
        seed.InventoryItems.Add(item);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(async index =>
                {
                    await using var context = TestContexts.Inventory(DataSource, Clock);
                    var current = await context.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);

                    // A writer that reads after the first commit sees an empty shelf: the domain itself refuses.
                    if (current.Adjust(-5, "damage", Guid.CreateVersion7(), $"key-race-000{index}", Clock).IsFailure)
                    {
                        return false;
                    }

                    try
                    {
                        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                        return true;
                    }
                    catch (PersistenceConflictException)
                    {
                        return false;
                    }
                })
        );

        outcomes.Count(won => won).ShouldBe(1);
        (await Database.ScalarAsync("SELECT on_hand FROM inventory.inventory_items")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_commit_a_retried_adjustment_with_the_same_idempotency_key_only_once()
    {
        var item = InventoryItem.Open(Sku.Create("CAF-600-PRT").Value, Clock);
        var seed = Inventory();
        seed.InventoryItems.Add(item);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var left = Inventory();
        var right = Inventory();
        var leftItem = await left.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);
        var rightItem = await right.InventoryItems.SingleAsync(TestContext.Current.CancellationToken);
        leftItem.Adjust(5, "stocktake", Guid.CreateVersion7(), "same-key-0001", Clock);
        rightItem.Adjust(5, "stocktake", Guid.CreateVersion7(), "same-key-0001", Clock);

        await left.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Should.ThrowAsync<PersistenceConflictException>(() =>
            right.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        (await Database.ScalarAsync("SELECT on_hand FROM inventory.inventory_items")).ShouldBe(5);
        (await Database.ScalarAsync("SELECT count(*) FROM inventory.stock_movements")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_let_only_one_of_two_requests_consume_the_same_refresh_token()
    {
        var user = User.Register(
            EmailAddress.Create("a@example.com").Value,
            "$argon2id$stub",
            AccessLevel.Public,
            Clock
        );
        var token = RefreshToken.Issue(
            user.Id,
            Guid.CreateVersion7(),
            "hash-1",
            Clock.GetUtcNow().AddDays(7),
            Clock.GetUtcNow().AddDays(30),
            Clock
        );
        var seed = Identity();
        seed.Users.Add(user);
        seed.RefreshTokens.Add(token);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var left = Identity();
        var right = Identity();
        var leftToken = await left.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken);
        var rightToken = await right.RefreshTokens.SingleAsync(TestContext.Current.CancellationToken);

        leftToken.Use(Clock).IsSuccess.ShouldBeTrue();
        rightToken.Use(Clock).IsSuccess.ShouldBeTrue();
        await left.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Across instances the persisted version, not the in-process flag, is what stops the second redemption.
        await Should.ThrowAsync<PersistenceConflictException>(() =>
            right.SaveChangesAsync(TestContext.Current.CancellationToken)
        );
    }
}
