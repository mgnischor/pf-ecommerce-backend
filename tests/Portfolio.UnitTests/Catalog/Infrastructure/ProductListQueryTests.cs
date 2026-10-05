using Microsoft.EntityFrameworkCore;
using Portfolio.Catalog.Domain;
using Portfolio.Catalog.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.Catalog.Infrastructure;

/// <summary>
/// The SQL of a product listing (BR-CAT-006). Translating a query needs the provider but never a connection, so the
/// keyset, search, and ordering that would otherwise surface only against a real database are checked here.
/// </summary>
[Trait("Rule", "BR-CAT-006")]
public sealed class ProductListQueryTests : IDisposable
{
    private static readonly Guid LastId = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01");

    private readonly CatalogDbContext _context = new(
        DesignTimeOptions.Create<CatalogDbContext>(CatalogDbContext.SchemaName)
    );

    public void Dispose() => _context.Dispose();

    private string Sql(ProductListCriteria criteria) =>
        new EfProductRepository(_context).BuildListQuery(criteria).ToQueryString();

    [Fact]
    public void Should_list_newest_first_with_the_identifier_as_tie_breaker_and_exclude_deleted_products()
    {
        var sql = Sql(new ProductListCriteria(ProductSortField.CreatedAt, Descending: true, Limit: 21));

        sql.ShouldContain("deleted_at IS NULL");
        sql.ShouldContain("ORDER BY p.created_at DESC, p.id DESC");
        sql.ShouldContain("-- @p='21'");
        sql.ShouldContain("LIMIT @p");
    }

    [Theory]
    [InlineData("Name", true, "ORDER BY p.name DESC, p.id DESC")]
    [InlineData("Name", false, "ORDER BY p.name, p.id")]
    [InlineData("Price", true, "ORDER BY p.price_amount DESC, p.id DESC")]
    [InlineData("Price", false, "ORDER BY p.price_amount, p.id")]
    [InlineData("CreatedAt", false, "ORDER BY p.created_at, p.id")]
    public void Should_order_by_the_requested_field_in_the_requested_direction(
        string field,
        bool descending,
        string expected
    )
    {
        Sql(new ProductListCriteria(Enum.Parse<ProductSortField>(field), descending, 10)).ShouldContain(expected);
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_item_when_listing_by_name()
    {
        var after = new ProductSeekPosition(LastId, "Cafeteira", null, null);

        var sql = Sql(new ProductListCriteria(ProductSortField.Name, false, 10, After: after));

        sql.ShouldContain("(p.name > @name OR (p.name = @name AND p.id > @");
        sql.ShouldContain("-- @name='Cafeteira'");
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_item_when_listing_by_price_descending()
    {
        var after = new ProductSeekPosition(LastId, null, 189.90m, null);

        var sql = Sql(new ProductListCriteria(ProductSortField.Price, true, 10, After: after));

        sql.ShouldContain("(p.price_amount < @amount OR (p.price_amount = @amount AND p.id < @");
        sql.ShouldContain("-- @amount='189.90'");
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_item_when_listing_by_creation()
    {
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var after = new ProductSeekPosition(LastId, null, null, createdAt);

        var sql = Sql(new ProductListCriteria(ProductSortField.CreatedAt, true, 10, After: after));

        sql.ShouldContain("(p.created_at < @createdAt OR (p.created_at = @createdAt AND p.id < @");
        sql.ShouldContain("-- @createdAt='2026-10-01T12:00:00.0000000+00:00'");
    }

    [Fact]
    public void Should_filter_by_status()
    {
        var sql = Sql(new ProductListCriteria(ProductSortField.CreatedAt, true, 10, Status: ProductStatus.Active));

        sql.ShouldContain("p.status = @status");
        sql.ShouldContain("-- @status='Active'");
    }

    [Fact]
    public void Should_search_the_name_case_insensitively_and_escape_the_pattern_characters()
    {
        var sql = Sql(new ProductListCriteria(ProductSortField.CreatedAt, true, 10, SearchText: "50%_off"));

        // Parameterized, never concatenated; the user's % and _ are escaped so they match literally.
        sql.ShouldContain(@"p.name ILIKE @pattern ESCAPE '\'");
        sql.ShouldContain(@"-- @pattern='%50\%\_off%'");
        sql.ShouldNotContain("OR p.sku");
    }

    [Fact]
    public void Should_also_match_an_exact_sku_when_the_text_is_a_valid_sku()
    {
        var sql = Sql(new ProductListCriteria(ProductSortField.CreatedAt, true, 10, SearchText: "caf-600-prt"));

        sql.ShouldContain("OR p.sku = @exactSku");
        sql.ShouldContain("-- @exactSku='CAF-600-PRT'");
    }

    [Fact]
    public void Should_reject_a_seek_position_that_does_not_match_the_sort_field()
    {
        var after = new ProductSeekPosition(LastId, "Cafeteira", null, null);

        Should.Throw<ArgumentException>(() =>
            Sql(new ProductListCriteria(ProductSortField.Price, false, 10, After: after))
        );
    }
}
