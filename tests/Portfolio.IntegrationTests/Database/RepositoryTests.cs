using Microsoft.EntityFrameworkCore;
using Portfolio.Catalog.Domain;
using Portfolio.Catalog.Infrastructure;
using Portfolio.Identity.Domain;
using Portfolio.Identity.Infrastructure;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Domain;
using CatalogSku = Portfolio.Catalog.Domain.Sku;
using InventorySku = Portfolio.Inventory.Domain.Sku;

namespace Portfolio.IntegrationTests.Database;

/// <summary>The EF Core repositories against real PostgreSQL: mappings, converters, filters, and the contracts the use cases rely on.</summary>
public sealed class RepositoryTests : DatabaseTestBase
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // ---- Inventory ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Should_find_an_inventory_item_by_its_normalized_sku()
    {
        var writer = Inventory();
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        await new EfInventoryItemRepository(writer).AddAsync(item, Cancel);
        await writer.SaveChangesAsync(Cancel);

        var found = await new EfInventoryItemRepository(Inventory()).FindBySkuAsync(
            InventorySku.Create(" caf-600-prt ").Value,
            Cancel
        );

        found.ShouldNotBeNull().Id.ShouldBe(item.Id);
        found.Sku.Value.ShouldBe("CAF-600-PRT");
        (
            await new EfInventoryItemRepository(Inventory()).FindBySkuAsync(
                InventorySku.Create("OTHER-0001").Value,
                Cancel
            )
        ).ShouldBeNull();
    }

    [Fact]
    public async Task Should_persist_stock_reservations_and_the_ledger_through_the_aggregate()
    {
        var writer = Inventory();
        var repository = new EfInventoryItemRepository(writer);
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        item.Adjust(10, "stocktake", Guid.CreateVersion7(), "key-0001-aaaa", Clock);
        item.Reserve(4, Clock);
        await repository.AddAsync(item, Cancel);
        await writer.SaveChangesAsync(Cancel);

        var reader = Inventory();
        var loaded = await new EfInventoryItemRepository(reader).GetByIdAsync(item.Id, Cancel);

        loaded.ShouldNotBeNull();
        (loaded.OnHand, loaded.Reserved, loaded.Available, loaded.Version).ShouldBe((10, 4, 6, item.Version));
        (await reader.StockMovements.CountAsync(Cancel)).ShouldBe(1);
    }

    [Fact]
    public async Task Should_append_a_movement_to_a_loaded_item_without_reading_its_history()
    {
        var seed = Inventory();
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        item.Adjust(10, "stocktake", Guid.CreateVersion7(), "key-0001-aaaa", Clock);
        seed.InventoryItems.Add(item);
        await seed.SaveChangesAsync(Cancel);

        var context = Inventory();
        var repository = new EfInventoryItemRepository(context);
        var loaded = await repository.GetByIdAsync(item.Id, Cancel);
        loaded.ShouldNotBeNull().Movements.ShouldBeEmpty();
        loaded.Adjust(-3, "damage", Guid.CreateVersion7(), "key-0002-bbbb", Clock).IsSuccess.ShouldBeTrue();
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        (
            await Database.StringsAsync("SELECT delta FROM inventory.stock_movements ORDER BY created_at, delta")
        ).ShouldBe(["-3", "10"]);
        (await Database.ScalarAsync("SELECT on_hand FROM inventory.inventory_items")).ShouldBe(7);
    }

    [Fact]
    public async Task Should_find_the_movement_a_client_key_already_recorded_and_only_that_one()
    {
        var seed = Inventory();
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        var recorded = item.Adjust(10, "stocktake", Guid.CreateVersion7(), "key-0001-aaaa", Clock).Value;
        seed.InventoryItems.Add(item);
        await seed.SaveChangesAsync(Cancel);
        var repository = new EfInventoryItemRepository(Inventory());

        var found = await repository.FindMovementAsync(item.Id, "key-0001-aaaa", Cancel);

        found.ShouldNotBeNull().Id.ShouldBe(recorded.Id);
        found.IsEquivalentTo(10, "stocktake").ShouldBeTrue();
        (await repository.FindMovementAsync(item.Id, "key-9999-zzzz", Cancel)).ShouldBeNull();
        (await repository.FindMovementAsync(Guid.CreateVersion7(), "key-0001-aaaa", Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_stop_returning_a_removed_item_and_free_its_sku()
    {
        var context = Inventory();
        var repository = new EfInventoryItemRepository(context);
        var item = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);
        await repository.AddAsync(item, Cancel);
        await context.SaveChangesAsync(Cancel);

        repository.Remove(item);
        await context.SaveChangesAsync(Cancel);

        var reader = new EfInventoryItemRepository(Inventory());
        (await reader.GetByIdAsync(item.Id, Cancel)).ShouldBeNull();
        (await reader.FindBySkuAsync(item.Sku, Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_refuse_to_update_an_aggregate_that_was_not_loaded_through_the_repository()
    {
        var detached = InventoryItem.Open(InventorySku.Create("CAF-600-PRT").Value, Clock);

        Should.Throw<InvalidOperationException>(() => new EfInventoryItemRepository(Inventory()).Update(detached));
        await Task.CompletedTask;
    }

    // ---- Catalog -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Should_find_a_product_by_id_and_by_sku_and_keep_its_value_objects()
    {
        var writer = Catalog();
        var product = NewProduct();
        await new EfProductRepository(writer).AddAsync(product, Cancel);
        await writer.SaveChangesAsync(Cancel);
        var repository = new EfProductRepository(Catalog());

        var byId = await repository.GetByIdAsync(product.Id, Cancel);
        var bySku = await new EfProductRepository(Catalog()).FindBySkuAsync(
            CatalogSku.Create("caf-600-prt").Value,
            Cancel
        );

        byId.ShouldNotBeNull();
        (byId.Name, byId.Description, byId.Price, byId.Status).ShouldBe(
            ("Cafeteira Elétrica 600ml", "Filtro permanente.", new Money(189.90m, "BRL"), ProductStatus.Draft)
        );
        bySku.ShouldNotBeNull().Id.ShouldBe(product.Id);
    }

    [Fact]
    public async Task Should_persist_a_lifecycle_change_made_to_a_loaded_product()
    {
        var seed = Catalog();
        var product = NewProduct();
        seed.Products.Add(product);
        await seed.SaveChangesAsync(Cancel);

        var context = Catalog();
        var repository = new EfProductRepository(context);
        var loaded = await repository.GetByIdAsync(product.Id, Cancel);
        loaded.ShouldNotBeNull().Activate(Clock).IsSuccess.ShouldBeTrue();
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        (await new EfProductRepository(Catalog()).GetByIdAsync(product.Id, Cancel))!.Status.ShouldBe(
            ProductStatus.Active
        );
    }

    // ---- Identity ----------------------------------------------------------------------------------------------

    private User NewUser(string email = "Staff.User@Example.com", AccessLevel level = AccessLevel.Manager) =>
        User.Register(EmailAddress.Create(email).Value, "$argon2id$v=19$m=65536,t=3,p=4$salt$hash", level, Clock);

    [Fact]
    public async Task Should_find_an_account_by_its_normalized_email_and_store_the_level_as_text()
    {
        var writer = Identity();
        var user = NewUser();
        await new EfUserRepository(writer).AddAsync(user, Cancel);
        await writer.SaveChangesAsync(Cancel);

        var found = await new EfUserRepository(Identity()).FindByEmailAsync(
            EmailAddress.Create("  STAFF.user@example.com ").Value,
            Cancel
        );

        found.ShouldNotBeNull().Id.ShouldBe(user.Id);
        (found.AccessLevel, found.Status, found.TokenVersion).ShouldBe((AccessLevel.Manager, UserStatus.Active, 1));
        (await Database.ScalarAsync("SELECT email FROM identity.users")).ShouldBe("staff.user@example.com");
        (await Database.ScalarAsync("SELECT access_level FROM identity.users")).ShouldBe("Manager");
    }

    [Fact]
    public async Task Should_persist_lockout_and_deactivation_state_of_an_account()
    {
        var seed = Identity();
        var user = NewUser();
        seed.Users.Add(user);
        await seed.SaveChangesAsync(Cancel);

        var context = Identity();
        var repository = new EfUserRepository(context);
        var loaded = (await repository.GetByIdAsync(user.Id, Cancel))!;
        for (var failure = 0; failure < User.MaxFailedSignIns; failure++)
        {
            loaded.RecordFailedSignIn(Clock);
        }

        loaded.Deactivate(Clock);
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var reloaded = (await new EfUserRepository(Identity()).GetByIdAsync(user.Id, Cancel))!;
        reloaded.IsLockedOut(Clock.GetUtcNow()).ShouldBeTrue();
        reloaded.LockedUntil.ShouldBe(Clock.GetUtcNow().Add(User.LockoutDuration));
        (reloaded.Status, reloaded.TokenVersion).ShouldBe((UserStatus.Deactivated, 2));
    }

    [Fact]
    public async Task Should_find_refresh_tokens_by_hash_family_and_account()
    {
        var user = NewUser();
        var family = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var writer = Identity();
        writer.Users.Add(user);
        foreach (var (hash, session) in new[] { ("hash-a", family), ("hash-b", family), ("hash-c", other) })
        {
            writer.RefreshTokens.Add(
                RefreshToken.Issue(
                    user.Id,
                    session,
                    hash,
                    Clock.GetUtcNow().AddDays(7),
                    Clock.GetUtcNow().AddDays(30),
                    Clock
                )
            );
        }

        await writer.SaveChangesAsync(Cancel);
        var repository = new EfRefreshTokenRepository(Identity());

        (await repository.FindByHashAsync("hash-b", Cancel)).ShouldNotBeNull().FamilyId.ShouldBe(family);
        (await repository.FindByHashAsync("missing", Cancel)).ShouldBeNull();
        (await repository.ListByFamilyAsync(family, Cancel))
            .Select(token => token.TokenHash)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["hash-a", "hash-b"]);
        (await repository.ListByUserAsync(user.Id, Cancel)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_persist_the_revocation_of_every_token_of_a_session()
    {
        var user = NewUser();
        var family = Guid.CreateVersion7();
        var seed = Identity();
        seed.Users.Add(user);
        seed.RefreshTokens.Add(
            RefreshToken.Issue(
                user.Id,
                family,
                "hash-a",
                Clock.GetUtcNow().AddDays(7),
                Clock.GetUtcNow().AddDays(30),
                Clock
            )
        );
        seed.RefreshTokens.Add(
            RefreshToken.Issue(
                user.Id,
                family,
                "hash-b",
                Clock.GetUtcNow().AddDays(7),
                Clock.GetUtcNow().AddDays(30),
                Clock
            )
        );
        await seed.SaveChangesAsync(Cancel);

        var context = Identity();
        var repository = new EfRefreshTokenRepository(context);
        foreach (var token in await repository.ListByFamilyAsync(family, Cancel))
        {
            token.Revoke(Clock);
            repository.Update(token);
        }

        await context.SaveChangesAsync(Cancel);

        (
            await Database.ScalarAsync("SELECT count(*) FROM identity.refresh_tokens WHERE revoked_at IS NOT NULL")
        ).ShouldBe(2L);
    }

    [Fact]
    public async Task Should_store_only_the_hash_of_a_refresh_token_never_anything_else()
    {
        var user = NewUser();
        var context = Identity();
        context.Users.Add(user);
        context.RefreshTokens.Add(
            RefreshToken.Issue(
                user.Id,
                Guid.CreateVersion7(),
                "only-the-hmac",
                Clock.GetUtcNow().AddDays(7),
                Clock.GetUtcNow().AddDays(30),
                Clock
            )
        );
        await context.SaveChangesAsync(Cancel);

        var columns = await Database.StringsAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'identity' AND table_name = 'refresh_tokens'"
        );

        columns.ShouldContain("token_hash");
        columns.ShouldNotContain("token");
        columns.ShouldNotContain("plain_token");
    }

    [Fact]
    public async Task Should_refuse_a_second_active_account_with_the_same_email_as_a_conflict()
    {
        var first = Identity();
        first.Users.Add(NewUser());
        await first.SaveChangesAsync(Cancel);

        var second = Identity();
        second.Users.Add(NewUser("staff.user@example.com"));

        var failure = await Should.ThrowAsync<Portfolio.SharedKernel.Application.PersistenceConflictException>(() =>
            second.SaveChangesAsync(Cancel)
        );
        failure.Code.ShouldBe("DUPLICATE_RECORD");
    }
}
