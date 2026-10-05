using Npgsql;
using Portfolio.Catalog.Domain;
using Portfolio.Catalog.Infrastructure;
using Portfolio.SharedKernel.Domain;
using CatalogSku = Portfolio.Catalog.Domain.Sku;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The product listing and the idempotency keys against real PostgreSQL (BR-CAT-006 to BR-CAT-008): keyset paging,
/// search, soft deletion, and the unique index that closes the race on a creation key.
/// </summary>
public sealed class CatalogRepositoryTests : DatabaseTestBase
{
    private const string UniqueViolation = "23505";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<Product> SeedAsync(
        string sku,
        string name = "Cafeteira Elétrica",
        decimal price = 100m,
        ProductStatus status = ProductStatus.Draft,
        string? creationKey = null
    )
    {
        var product = Product
            .Create(name, CatalogSku.Create(sku).Value, new Money(price, "BRL"), null, Clock, creationKey)
            .Value;
        if (status is ProductStatus.Active or ProductStatus.Discontinued)
        {
            product.Activate(Clock);
        }

        if (status is ProductStatus.Discontinued)
        {
            product.Discontinue(Clock);
        }

        var context = Catalog();
        context.Products.Add(product);
        await context.SaveChangesAsync(Cancel);

        // Distinct creation instants keep the default order deterministic.
        Clock.Advance(TimeSpan.FromSeconds(1));
        return product;
    }

    private async Task DeleteAsync(Guid id, string key)
    {
        var context = Catalog();
        var repository = new EfProductRepository(context);
        var product = (await repository.GetByIdAsync(id, Cancel)).ShouldNotBeNull();
        product.Delete(key, Clock);
        repository.Update(product);
        await context.SaveChangesAsync(Cancel);
    }

    private async Task<IReadOnlyList<Product>> ListAsync(ProductListCriteria criteria) =>
        await new EfProductRepository(Catalog()).ListAsync(criteria, Cancel);

    private static ProductListCriteria Criteria(
        ProductSortField field = ProductSortField.CreatedAt,
        bool descending = true,
        int limit = 50,
        string? search = null,
        ProductStatus? status = null,
        ProductSeekPosition? after = null
    ) => new(field, descending, limit, search, status, after);

    private static string[] Skus(IEnumerable<Product> products) => [.. products.Select(product => product.Sku.Value)];

    // ---- Idempotency keys (BR-CAT-008) -------------------------------------------------------------------------

    [Fact]
    public async Task Should_find_the_product_a_creation_key_created_and_nothing_for_an_unused_key()
    {
        var product = await SeedAsync("CAF-0001", creationKey: "key-0001-aaaa");

        var repository = new EfProductRepository(Catalog());
        var found = await repository.FindByCreationKeyAsync("key-0001-aaaa", Cancel);
        var unused = await repository.FindByCreationKeyAsync("key-9999-zzzz", Cancel);

        found.ShouldNotBeNull().Id.ShouldBe(product.Id);
        found.CreationKey.ShouldBe("key-0001-aaaa");
        unused.ShouldBeNull();
    }

    [Fact]
    public async Task Should_still_find_a_product_by_its_creation_key_after_it_was_deleted()
    {
        var product = await SeedAsync("CAF-0001", creationKey: "key-0001-aaaa");
        await DeleteAsync(product.Id, "key-0002-bbbb");

        var found = await new EfProductRepository(Catalog()).FindByCreationKeyAsync("key-0001-aaaa", Cancel);

        found.ShouldNotBeNull().IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_refuse_two_products_with_the_same_creation_key_even_when_one_was_deleted()
    {
        var first = await SeedAsync("CAF-0001", creationKey: "key-0001-aaaa");
        await DeleteAsync(first.Id, "key-0002-bbbb");

        var duplicate = await Should.ThrowAsync<Exception>(() => SeedAsync("CAF-0002", creationKey: "key-0001-aaaa"));

        FindPostgresError(duplicate).SqlState.ShouldBe(UniqueViolation);
        FindPostgresError(duplicate).ConstraintName.ShouldBe("ux_products_creation_key");
    }

    [Fact]
    public async Task Should_allow_any_number_of_products_created_without_a_key()
    {
        await SeedAsync("CAF-0001");
        await SeedAsync("CAF-0002");

        (await Database.ScalarAsync("SELECT count(*) FROM catalog.products WHERE creation_key IS NULL")).ShouldBe(2L);
    }

    [Fact]
    public async Task Should_tell_whether_a_deletion_was_made_by_the_request_with_that_key()
    {
        var deleted = await SeedAsync("CAF-0001");
        var kept = await SeedAsync("CAF-0002");
        await DeleteAsync(deleted.Id, "key-0002-bbbb");
        var repository = new EfProductRepository(Catalog());

        (await repository.WasDeletedByAsync(deleted.Id, "key-0002-bbbb", Cancel)).ShouldBeTrue();
        (await repository.WasDeletedByAsync(deleted.Id, "key-0003-cccc", Cancel)).ShouldBeFalse();
        (await repository.WasDeletedByAsync(kept.Id, "key-0002-bbbb", Cancel)).ShouldBeFalse();
        (await repository.WasDeletedByAsync(Guid.CreateVersion7(), "key-0002-bbbb", Cancel)).ShouldBeFalse();
    }

    // ---- Logical deletion (BR-CAT-007) -------------------------------------------------------------------------

    [Fact]
    public async Task Should_hide_a_deleted_product_keep_its_row_and_release_its_sku()
    {
        var product = await SeedAsync("CAF-0001", status: ProductStatus.Active);
        await DeleteAsync(product.Id, "key-0002-bbbb");
        var repository = new EfProductRepository(Catalog());

        (await repository.GetByIdAsync(product.Id, Cancel)).ShouldBeNull();
        (await repository.FindBySkuAsync(product.Sku, Cancel)).ShouldBeNull();
        (await ListAsync(Criteria())).ShouldBeEmpty();
        (await Database.ScalarAsync($"SELECT count(*) FROM catalog.products WHERE id = '{product.Id}'")).ShouldBe(1L);
        (await Database.ScalarAsync($"SELECT deletion_key FROM catalog.products WHERE id = '{product.Id}'")).ShouldBe(
            "key-0002-bbbb"
        );

        // The partial unique index lets another product take the SKU.
        var reused = await SeedAsync("CAF-0001");
        (await repository.FindBySkuAsync(reused.Sku, Cancel)).ShouldNotBeNull().Id.ShouldBe(reused.Id);
    }

    [Fact]
    public async Task Should_advance_the_version_of_a_deleted_product()
    {
        var product = await SeedAsync("CAF-0001");

        await DeleteAsync(product.Id, "key-0002-bbbb");

        (await Database.ScalarAsync($"SELECT version FROM catalog.products WHERE id = '{product.Id}'")).ShouldBe(2);
    }

    // ---- Listing (BR-CAT-006) ----------------------------------------------------------------------------------

    [Fact]
    public async Task Should_list_newest_first_by_default_and_oldest_first_when_ascending()
    {
        await SeedAsync("SKU-0001");
        await SeedAsync("SKU-0002");
        await SeedAsync("SKU-0003");

        Skus(await ListAsync(Criteria())).ShouldBe(["SKU-0003", "SKU-0002", "SKU-0001"]);
        Skus(await ListAsync(Criteria(descending: false))).ShouldBe(["SKU-0001", "SKU-0002", "SKU-0003"]);
    }

    [Fact]
    public async Task Should_order_by_name_and_by_price_in_both_directions()
    {
        await SeedAsync("SKU-0001", "Banana", 30m);
        await SeedAsync("SKU-0002", "Abacate", 20m);
        await SeedAsync("SKU-0003", "Cereja", 10m);

        Skus(await ListAsync(Criteria(ProductSortField.Name, descending: false)))
            .ShouldBe(["SKU-0002", "SKU-0001", "SKU-0003"]);
        Skus(await ListAsync(Criteria(ProductSortField.Name, descending: true)))
            .ShouldBe(["SKU-0003", "SKU-0001", "SKU-0002"]);
        Skus(await ListAsync(Criteria(ProductSortField.Price, descending: false)))
            .ShouldBe(["SKU-0003", "SKU-0002", "SKU-0001"]);
        Skus(await ListAsync(Criteria(ProductSortField.Price, descending: true)))
            .ShouldBe(["SKU-0001", "SKU-0002", "SKU-0003"]);
    }

    [Theory]
    [InlineData("CreatedAt", true)]
    [InlineData("CreatedAt", false)]
    [InlineData("Name", true)]
    [InlineData("Name", false)]
    [InlineData("Price", true)]
    [InlineData("Price", false)]
    public async Task Should_walk_every_page_in_order_without_a_duplicate_or_a_gap_even_when_sort_values_tie(
        string fieldName,
        bool descending
    )
    {
        var field = Enum.Parse<ProductSortField>(fieldName);

        // Three pairs share the same name and the same price: only the identifier tells them apart.
        foreach (var index in Enumerable.Range(1, 7))
        {
            await SeedAsync($"SKU-{index:0000}", $"Produto {index / 2}", 10m * ((index / 2) + 1));
        }

        var everything = Skus(await ListAsync(Criteria(field, descending)));
        var walked = new List<string>();
        ProductSeekPosition? after = null;
        for (var page = 0; page < 10; page++)
        {
            var found = await ListAsync(Criteria(field, descending, limit: 3, after: after));
            if (found.Count == 0)
            {
                break;
            }

            walked.AddRange(Skus(found));
            after = SeekAfter(found[^1], field);
        }

        walked.ShouldBe(everything);
        walked.Distinct(StringComparer.Ordinal).Count().ShouldBe(7);
    }

    [Fact]
    public async Task Should_cap_the_page_at_the_requested_limit()
    {
        foreach (var index in Enumerable.Range(1, 5))
        {
            await SeedAsync($"SKU-{index:0000}");
        }

        (await ListAsync(Criteria(limit: 2))).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Should_filter_by_status()
    {
        await SeedAsync("SKU-0001", status: ProductStatus.Draft);
        await SeedAsync("SKU-0002", status: ProductStatus.Active);
        await SeedAsync("SKU-0003", status: ProductStatus.Discontinued);

        Skus(await ListAsync(Criteria(status: ProductStatus.Active))).ShouldBe(["SKU-0002"]);
        Skus(await ListAsync(Criteria(status: ProductStatus.Draft))).ShouldBe(["SKU-0001"]);
        (await ListAsync(Criteria())).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Should_search_the_name_case_insensitively_as_a_substring()
    {
        await SeedAsync("SKU-0001", "Cafeteira Elétrica 600ml");
        await SeedAsync("SKU-0002", "Liquidificador");

        Skus(await ListAsync(Criteria(search: "CAFETEIRA"))).ShouldBe(["SKU-0001"]);
        Skus(await ListAsync(Criteria(search: "trica 6"))).ShouldBe(["SKU-0001"]);
        (await ListAsync(Criteria(search: "inexistente"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_match_percent_underscore_and_backslash_literally_in_the_search()
    {
        await SeedAsync("SKU-0001", "Oferta 50% off");
        await SeedAsync("SKU-0002", "Oferta 50 off");
        await SeedAsync("SKU-0003", "snake_case item");
        await SeedAsync("SKU-0004", "snakeXcase item");
        await SeedAsync("SKU-0005", @"caminho\item");

        Skus(await ListAsync(Criteria(search: "50%"))).ShouldBe(["SKU-0001"]);
        Skus(await ListAsync(Criteria(search: "e_c"))).ShouldBe(["SKU-0003"]);
        Skus(await ListAsync(Criteria(search: @"o\i"))).ShouldBe(["SKU-0005"]);
        (await ListAsync(Criteria(search: "%%"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_find_a_product_by_its_exact_sku_but_not_by_part_of_it()
    {
        await SeedAsync("CAF-600-PRT", "Cafeteira");
        await SeedAsync("CAF-600-BRN", "Outra");

        Skus(await ListAsync(Criteria(search: "caf-600-prt"))).ShouldBe(["CAF-600-PRT"]);
        (await ListAsync(Criteria(search: "600-PRT"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_not_track_the_listed_products()
    {
        await SeedAsync("SKU-0001");
        var context = Catalog();

        await new EfProductRepository(context).ListAsync(Criteria(), Cancel);

        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_round_trip_a_price_scale_through_the_keyset()
    {
        await SeedAsync("SKU-0001", price: 10.0001m);
        await SeedAsync("SKU-0002", price: 10.0002m);
        await SeedAsync("SKU-0003", price: 10.0003m);

        var firstPage = await ListAsync(Criteria(ProductSortField.Price, descending: false, limit: 1));
        var secondPage = await ListAsync(
            Criteria(
                ProductSortField.Price,
                descending: false,
                limit: 5,
                after: SeekAfter(firstPage[0], ProductSortField.Price)
            )
        );

        Skus(secondPage).ShouldBe(["SKU-0002", "SKU-0003"]);
    }

    private static ProductSeekPosition SeekAfter(Product last, ProductSortField field) =>
        field switch
        {
            ProductSortField.Name => new ProductSeekPosition(last.Id, last.Name, null, null),
            ProductSortField.Price => new ProductSeekPosition(last.Id, null, last.Price.Amount, null),
            _ => new ProductSeekPosition(last.Id, null, null, last.CreatedAt),
        };

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
}
