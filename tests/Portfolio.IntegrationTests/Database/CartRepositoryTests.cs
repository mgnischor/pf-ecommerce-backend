using Npgsql;
using Portfolio.Cart.Domain;
using Portfolio.Cart.Infrastructure;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The Cart's persistence against real PostgreSQL (BR-CRT-001 to BR-CRT-005): carts with their lines, the logical
/// removal of a line, the partial unique indexes, optimistic concurrency, and the catalog view.
/// </summary>
public sealed class CartRepositoryTests : DatabaseTestBase
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<ShoppingCart> SeedCartAsync(Guid? customer = null, string currency = "BRL", int lines = 0)
    {
        var cart = ShoppingCart.Open(customer ?? Guid.CreateVersion7(), currency, Clock).Value;
        for (var index = 0; index < lines; index++)
        {
            cart.AddItem(Guid.CreateVersion7(), index + 1, Clock);
        }

        var context = Cart();
        await new EfShoppingCartRepository(context).AddAsync(cart, Cancel);
        await context.SaveChangesAsync(Cancel);
        return cart;
    }

    private static PostgresException FindPostgresError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        throw new InvalidOperationException("No PostgreSQL error in the exception chain.", exception);
    }

    // ---- Carts and lines ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Should_persist_a_cart_with_its_lines_and_load_them_back()
    {
        var cart = await SeedCartAsync(lines: 2);

        var loaded = await new EfShoppingCartRepository(Cart()).GetByIdAsync(cart.Id, Cancel);

        loaded.ShouldNotBeNull();
        (loaded.CustomerId, loaded.Currency, loaded.Status, loaded.Version).ShouldBe(
            (cart.CustomerId, "BRL", CartStatus.Active, cart.Version)
        );
        loaded
            .Items.Select(item => (item.ProductId, item.Quantity))
            .Order()
            .ShouldBe(cart.Items.Select(item => (item.ProductId, item.Quantity)).Order());
    }

    [Fact]
    public async Task Should_find_the_active_cart_of_a_customer_with_its_lines_and_nothing_for_another_customer()
    {
        var cart = await SeedCartAsync(lines: 1);
        var repository = new EfShoppingCartRepository(Cart());

        var found = await repository.FindActiveByCustomerAsync(cart.CustomerId, Cancel);
        var other = await repository.FindActiveByCustomerAsync(Guid.CreateVersion7(), Cancel);

        found.ShouldNotBeNull().Id.ShouldBe(cart.Id);
        found.Items.Count.ShouldBe(1);
        other.ShouldBeNull();
    }

    [Fact]
    public async Task Should_add_a_line_and_raise_the_quantity_of_an_existing_one_in_a_later_unit_of_work()
    {
        var cart = await SeedCartAsync(lines: 1);
        var existing = cart.Items.Single();
        var newProduct = Guid.CreateVersion7();

        var context = Cart();
        var repository = new EfShoppingCartRepository(context);
        var loaded = (await repository.GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        loaded.AddItem(existing.ProductId, 4, Clock);
        loaded.AddItem(newProduct, 2, Clock);
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var reloaded = (await new EfShoppingCartRepository(Cart()).GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        reloaded.Items.Single(item => item.ProductId == existing.ProductId).Quantity.ShouldBe(existing.Quantity + 4);
        reloaded.Items.Single(item => item.ProductId == newProduct).Quantity.ShouldBe(2);
        reloaded.Version.ShouldBe(cart.Version + 2);
    }

    [Fact]
    public async Task Should_remove_a_line_logically_keeping_its_row_and_advancing_the_cart_version()
    {
        var cart = await SeedCartAsync(lines: 2);
        var removed = cart.Items.First();

        var context = Cart();
        var repository = new EfShoppingCartRepository(context);
        var loaded = (await repository.GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        loaded.RemoveItem(removed.Id, Clock).IsSuccess.ShouldBeTrue();
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var reloaded = (await new EfShoppingCartRepository(Cart()).GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        reloaded.Items.ShouldHaveSingleItem().Id.ShouldNotBe(removed.Id);
        reloaded.Version.ShouldBe(cart.Version + 1);
        (await Database.ScalarAsync($"SELECT count(*) FROM cart.cart_items WHERE id = '{removed.Id}'")).ShouldBe(1L);
        (
            await Database.ScalarAsync(
                $"SELECT count(*) FROM cart.cart_items WHERE id = '{removed.Id}' AND deleted_at IS NOT NULL"
            )
        ).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_let_a_removed_product_be_added_again_because_the_unique_index_ignores_removed_lines()
    {
        var cart = await SeedCartAsync(lines: 1);
        var line = cart.Items.Single();

        var context = Cart();
        var repository = new EfShoppingCartRepository(context);
        var loaded = (await repository.GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        loaded.RemoveItem(line.Id, Clock);
        loaded.AddItem(line.ProductId, 3, Clock);
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var reloaded = (await new EfShoppingCartRepository(Cart()).GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        var again = reloaded.Items.ShouldHaveSingleItem();
        (again.ProductId, again.Quantity).ShouldBe((line.ProductId, 3));
        again.Id.ShouldNotBe(line.Id);
    }

    // ---- BR-CRT-001: one active cart per customer --------------------------------------------------------------

    [Fact]
    public async Task Should_refuse_a_second_active_cart_for_the_same_customer()
    {
        var first = await SeedCartAsync();

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() => SeedCartAsync(first.CustomerId));

        FindPostgresError(failure).SqlState.ShouldBe(UniqueViolation);
        FindPostgresError(failure).ConstraintName.ShouldBe("ux_carts_customer_active");
    }

    [Fact]
    public async Task Should_allow_a_new_active_cart_once_the_previous_one_left_the_active_status()
    {
        var first = await SeedCartAsync(lines: 1);
        var context = Cart();
        var repository = new EfShoppingCartRepository(context);
        var loaded = (await repository.GetByIdAsync(first.Id, Cancel)).ShouldNotBeNull();
        loaded.MarkCheckedOut(Clock).IsSuccess.ShouldBeTrue();
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var second = await SeedCartAsync(first.CustomerId);

        (await new EfShoppingCartRepository(Cart()).FindActiveByCustomerAsync(first.CustomerId, Cancel))
            .ShouldNotBeNull()
            .Id.ShouldBe(second.Id);
    }

    [Fact]
    public async Task Should_not_find_a_checked_out_cart_as_the_active_one()
    {
        var cart = await SeedCartAsync(lines: 1);
        var context = Cart();
        var repository = new EfShoppingCartRepository(context);
        var loaded = (await repository.GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        loaded.MarkCheckedOut(Clock);
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        (await new EfShoppingCartRepository(Cart()).FindActiveByCustomerAsync(cart.CustomerId, Cancel)).ShouldBeNull();
    }

    // ---- Concurrency (ai/DATABASE.md §2.2) ---------------------------------------------------------------------

    [Fact]
    public async Task Should_let_only_one_of_two_concurrent_changes_to_a_cart_win()
    {
        var cart = await SeedCartAsync();
        var firstContext = Cart();
        var secondContext = Cart();
        var first = (await new EfShoppingCartRepository(firstContext).GetByIdAsync(cart.Id, Cancel)).ShouldNotBeNull();
        var second = (
            await new EfShoppingCartRepository(secondContext).GetByIdAsync(cart.Id, Cancel)
        ).ShouldNotBeNull();

        first.AddItem(Guid.CreateVersion7(), 1, Clock);
        second.AddItem(Guid.CreateVersion7(), 1, Clock);
        await firstContext.SaveChangesAsync(Cancel);

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            secondContext.SaveChangesAsync(Cancel)
        );
        failure.Kind.ShouldBe(PersistenceConflictKind.ConcurrentUpdate);
        (await Database.ScalarAsync($"SELECT count(*) FROM cart.cart_items WHERE cart_id = '{cart.Id}'")).ShouldBe(1L);
    }

    // ---- Constraints the database enforces whatever the application does ---------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Should_refuse_a_line_quantity_outside_the_range_even_when_written_by_hand(int quantity)
    {
        var cart = await SeedCartAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync(
                $"""
                INSERT INTO cart.cart_items (id, cart_id, product_id, quantity, created_at, updated_at)
                VALUES ('{Guid.CreateVersion7()}', '{cart.Id}', '{Guid.CreateVersion7()}', {quantity}, now(), now())
                """
            )
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    [Fact]
    public async Task Should_refuse_an_unknown_cart_status_even_when_written_by_hand()
    {
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync(
                $"""
                INSERT INTO cart.carts (id, customer_id, currency, status, created_at, updated_at, version)
                VALUES ('{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', 'BRL', 'Abandoned', now(), now(), 1)
                """
            )
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    // ---- The Cart's view of the catalog ------------------------------------------------------------------------

    [Fact]
    public async Task Should_persist_the_catalog_view_with_its_price_and_per_facet_versions()
    {
        var product = CatalogProduct.Register(
            Guid.CreateVersion7(),
            "CAF-600-PRT",
            "Cafeteira Elétrica",
            new Money(189.90m, "BRL"),
            1,
            Clock
        );
        product.ApplyStatus(sellable: true, 3, Clock);
        var writer = Cart();
        await new EfCatalogProductRepository(writer).AddAsync(product, Cancel);
        await writer.SaveChangesAsync(Cancel);

        var loaded = (await new EfCatalogProductRepository(Cart()).GetByIdAsync(product.Id, Cancel)).ShouldNotBeNull();

        (loaded.Sku, loaded.Name, loaded.Sellable, loaded.PriceVersion, loaded.StatusVersion).ShouldBe(
            ("CAF-600-PRT", "Cafeteira Elétrica", true, 1, 3)
        );
        loaded.Price.ShouldBe(new Money(189.90m, "BRL"));
    }

    [Fact]
    public async Task Should_load_several_products_at_once_and_skip_the_unknown_ones()
    {
        var known = new List<CatalogProduct>();
        var writer = Cart();
        for (var index = 0; index < 3; index++)
        {
            var product = CatalogProduct.Register(
                Guid.CreateVersion7(),
                $"SKU-{index:0000}",
                $"Produto {index}",
                new Money(10m + index, "BRL"),
                1,
                Clock
            );
            known.Add(product);
            await new EfCatalogProductRepository(writer).AddAsync(product, Cancel);
        }

        await writer.SaveChangesAsync(Cancel);

        var found = await new EfCatalogProductRepository(Cart()).GetManyAsync(
            [known[0].Id, known[2].Id, Guid.CreateVersion7(), known[0].Id],
            Cancel
        );

        found.Keys.Order().ShouldBe(new[] { known[0].Id, known[2].Id }.Order());
        (await new EfCatalogProductRepository(Cart()).GetManyAsync([], Cancel)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_not_track_the_products_loaded_for_pricing()
    {
        var product = CatalogProduct.Register(
            Guid.CreateVersion7(),
            "SKU-0001",
            "Produto",
            new Money(1m, "BRL"),
            1,
            Clock
        );
        var writer = Cart();
        await new EfCatalogProductRepository(writer).AddAsync(product, Cancel);
        await writer.SaveChangesAsync(Cancel);
        var reader = Cart();

        await new EfCatalogProductRepository(reader).GetManyAsync([product.Id], Cancel);

        reader.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_refuse_a_catalog_product_priced_at_zero_even_when_written_by_hand()
    {
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync(
                $"""
                INSERT INTO cart.catalog_products (id, sku, name, sellable, price_version, status_version, price_amount, price_currency, created_at, updated_at, version)
                VALUES ('{Guid.CreateVersion7()}', 'SKU-0001', 'Produto', false, 1, 1, 0, 'BRL', now(), now(), 1)
                """
            )
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    [Fact]
    public async Task Should_let_only_one_of_two_concurrent_updates_of_a_catalog_product_win()
    {
        var product = CatalogProduct.Register(
            Guid.CreateVersion7(),
            "SKU-0001",
            "Produto",
            new Money(10m, "BRL"),
            1,
            Clock
        );
        var writer = Cart();
        await new EfCatalogProductRepository(writer).AddAsync(product, Cancel);
        await writer.SaveChangesAsync(Cancel);
        var firstContext = Cart();
        var secondContext = Cart();
        var first = (
            await new EfCatalogProductRepository(firstContext).GetByIdAsync(product.Id, Cancel)
        ).ShouldNotBeNull();
        var second = (
            await new EfCatalogProductRepository(secondContext).GetByIdAsync(product.Id, Cancel)
        ).ShouldNotBeNull();

        first.ApplyPrice(new Money(11m, "BRL"), 2, Clock);
        second.ApplyStatus(sellable: true, 2, Clock);
        await firstContext.SaveChangesAsync(Cancel);

        // The loser is retried by the consumer, finds the winner's row, and applies its own facet on top of it.
        (
            await Should.ThrowAsync<PersistenceConflictException>(() => secondContext.SaveChangesAsync(Cancel))
        ).Kind.ShouldBe(PersistenceConflictKind.ConcurrentUpdate);
        var reloaded = (
            await new EfCatalogProductRepository(Cart()).GetByIdAsync(product.Id, Cancel)
        ).ShouldNotBeNull();
        (reloaded.Price.Amount, reloaded.Sellable).ShouldBe((11m, false));
    }
}
