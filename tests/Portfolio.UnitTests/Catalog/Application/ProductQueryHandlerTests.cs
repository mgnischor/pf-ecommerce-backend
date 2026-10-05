using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Catalog.Application;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.UnitTests.Catalog.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Application;

[Trait("Rule", "BR-CAT-006")]
public sealed class GetProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly GetProductHandler _handler;

    public GetProductHandlerTests()
    {
        _handler = new GetProductHandler(_products);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Active")]
    [InlineData("Discontinued")]
    public async Task Should_show_a_product_in_every_status_to_catalog_staff(string statusName)
    {
        var product = ProductBuilder.New().WithStatus(Enum.Parse<ProductStatus>(statusName)).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new GetProductQuery(product.Id, CanSeeAllStatuses: true),
            TestContext.Current.CancellationToken
        );

        result.Value.Id.ShouldBe(product.Id);
        result.Value.Status.ShouldBe(Enum.Parse<ProductStatus>(statusName).ToView());
    }

    [Fact]
    public async Task Should_show_an_active_product_to_the_public_with_its_server_side_price_and_version()
    {
        var product = ProductBuilder.New().WithStatus(ProductStatus.Active).WithPrice(189.90m).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new GetProductQuery(product.Id, CanSeeAllStatuses: false),
            TestContext.Current.CancellationToken
        );

        result.Value.PriceAmount.ShouldBe(189.90m);
        result.Value.PriceCurrency.ShouldBe("BRL");
        result.Value.Version.ShouldBe(product.Version);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Discontinued")]
    public async Task Should_answer_not_found_for_the_public_when_the_product_is_not_active(string statusName)
    {
        var product = ProductBuilder.New().WithStatus(Enum.Parse<ProductStatus>(statusName)).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new GetProductQuery(product.Id, CanSeeAllStatuses: false),
            TestContext.Current.CancellationToken
        );

        // Exactly the answer for a product that does not exist, so its existence is not revealed.
        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_answer_not_found_when_the_product_does_not_exist()
    {
        var result = await _handler.HandleAsync(
            new GetProductQuery(Guid.CreateVersion7(), CanSeeAllStatuses: true),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Should_answer_not_found_when_the_product_was_deleted()
    {
        var product = ProductBuilder.New().WithStatus(ProductStatus.Active).Build(_clock);
        product.Delete("key-0001", _clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new GetProductQuery(product.Id, CanSeeAllStatuses: true),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
    }
}

[Trait("Rule", "BR-CAT-006")]
public sealed class ListProductsHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly HmacPageCursorCodec _cursors = new(
        Options.Create(
            new PageCursorOptions { CursorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }
        ),
        environmentAllowsEphemeralKey: false,
        NullLogger<HmacPageCursorCodec>.Instance
    );
    private readonly ListProductsHandler _handler;

    public ListProductsHandlerTests()
    {
        _handler = new ListProductsHandler(_products, _cursors);
    }

    private static ListProductsQuery Query(
        int limit = 20,
        string? cursor = null,
        string? search = null,
        string? sort = null,
        ProductStatusView? status = null,
        bool staff = true
    ) => new(limit, cursor, search, sort, status, staff);

    private void SeedListing(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _products.ListResult.Add(
                ProductBuilder
                    .New()
                    .WithSku($"SKU-{i:0000}")
                    .WithName($"Produto {i:00}")
                    .WithPrice(10m * i)
                    .Build(_clock)
            );
        }
    }

    private Task<Result<ProductPage>> ListAsync(ListProductsQuery query) =>
        _handler.HandleAsync(query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_list_newest_first_by_default_asking_for_one_extra_row_to_know_whether_more_exist()
    {
        var result = await ListAsync(Query());

        result.IsSuccess.ShouldBeTrue();
        var criteria = _products.LastCriteria.ShouldNotBeNull();
        criteria.SortField.ShouldBe(ProductSortField.CreatedAt);
        criteria.Descending.ShouldBeTrue();
        criteria.Limit.ShouldBe(21);
        criteria.After.ShouldBeNull();
    }

    [Theory]
    [InlineData("name", "Name", false)]
    [InlineData("-name", "Name", true)]
    [InlineData("price", "Price", false)]
    [InlineData("-price", "Price", true)]
    [InlineData("createdAt", "CreatedAt", false)]
    [InlineData("-createdAt", "CreatedAt", true)]
    public async Task Should_order_by_the_allowed_sort_field_and_direction(string sort, string field, bool descending)
    {
        await ListAsync(Query(sort: sort));

        var criteria = _products.LastCriteria.ShouldNotBeNull();
        criteria.SortField.ShouldBe(Enum.Parse<ProductSortField>(field));
        criteria.Descending.ShouldBe(descending);
    }

    [Theory]
    [InlineData("sku")]
    [InlineData("-sku")]
    [InlineData("Name")]
    [InlineData("createdat")]
    [InlineData("1")]
    [InlineData("--price")]
    public async Task Should_reject_a_sort_field_outside_the_allowlist_as_a_malformed_request(string sort)
    {
        var result = await ListAsync(Query(sort: sort));

        result.ShouldFail().Code.ShouldBe("SORT_FIELD_NOT_ALLOWED");
        result.ShouldFail().Type.ShouldBe(ErrorType.BadRequest);
        result.ShouldFail().Field.ShouldBe("sort");
        _products.LastCriteria.ShouldBeNull();
    }

    [Theory]
    [InlineData(-5, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(100, 101)]
    [InlineData(5000, 101)]
    public async Task Should_clamp_the_page_size_to_the_documented_range(int limit, int expectedRowsRequested)
    {
        await ListAsync(Query(limit: limit));

        _products.LastCriteria.ShouldNotBeNull().Limit.ShouldBe(expectedRowsRequested);
    }

    [Fact]
    public async Task Should_answer_an_empty_page_with_no_cursor_when_nothing_matches()
    {
        var result = await ListAsync(Query());

        result.Value.Items.ShouldBeEmpty();
        result.Value.HasMore.ShouldBeFalse();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Should_answer_the_last_page_without_a_cursor_when_the_rows_fit_exactly()
    {
        SeedListing(3);

        var result = await ListAsync(Query(limit: 3));

        result.Value.Items.Count.ShouldBe(3);
        result.Value.HasMore.ShouldBeFalse();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Should_trim_the_page_and_issue_a_cursor_when_more_rows_exist()
    {
        SeedListing(3);

        var result = await ListAsync(Query(limit: 2));

        result.Value.Items.Count.ShouldBe(2);
        result.Value.Items.Select(item => item.Sku).ShouldBe(["SKU-0001", "SKU-0002"]);
        result.Value.HasMore.ShouldBeTrue();
        result.Value.NextCursor.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("-createdAt")]
    [InlineData("name")]
    [InlineData("-price")]
    public async Task Should_continue_after_the_last_item_of_the_previous_page(string sort)
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2, sort: sort));
        var last = _products.ListResult[1];

        await ListAsync(Query(limit: 2, sort: sort, cursor: first.Value.NextCursor));

        var after = _products.LastCriteria.ShouldNotBeNull().After.ShouldNotBeNull();
        after.Id.ShouldBe(last.Id);
        after.Name.ShouldBe(sort == "name" ? last.Name : null);
        after.PriceAmount.ShouldBe(sort == "-price" ? last.Price.Amount : null);
        after.CreatedAt.ShouldBe(sort == "-createdAt" ? last.CreatedAt : null);
    }

    [Fact]
    public async Task Should_round_trip_a_name_that_contains_the_position_separator()
    {
        _products.ListResult.Add(ProductBuilder.New().WithSku("SKU-0001").WithName("A|B|C").Build(_clock));
        _products.ListResult.Add(ProductBuilder.New().WithSku("SKU-0002").WithName("Delta").Build(_clock));
        var first = await ListAsync(Query(limit: 1, sort: "name"));

        await ListAsync(Query(limit: 1, sort: "name", cursor: first.Value.NextCursor));

        _products.LastCriteria.ShouldNotBeNull().After.ShouldNotBeNull().Name.ShouldBe("A|B|C");
    }

    [Fact]
    public async Task Should_reject_a_forged_cursor_as_a_malformed_request()
    {
        var result = await ListAsync(Query(cursor: "bm90LWEtY3Vyc29y.AAAA"));

        result.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        result.ShouldFail().Type.ShouldBe(ErrorType.BadRequest);
        result.ShouldFail().Field.ShouldBe("cursor");
        _products.LastCriteria.ShouldBeNull();
    }

    [Fact]
    public async Task Should_reject_a_cursor_that_was_issued_for_another_sort()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2, sort: "name"));

        var result = await ListAsync(Query(limit: 2, sort: "price", cursor: first.Value.NextCursor));

        result.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
    }

    [Fact]
    public async Task Should_reject_a_cursor_that_was_issued_for_other_filters()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2, search: "produto"));

        var otherSearch = await ListAsync(Query(limit: 2, search: "outro", cursor: first.Value.NextCursor));
        var otherStatus = await ListAsync(
            Query(limit: 2, search: "produto", status: ProductStatusView.Draft, cursor: first.Value.NextCursor)
        );

        otherSearch.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        otherStatus.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
    }

    [Fact]
    public async Task Should_accept_a_cursor_with_the_same_filters_whatever_the_page_size()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 1, search: "produto"));

        var next = await ListAsync(Query(limit: 2, search: "produto", cursor: first.Value.NextCursor));

        next.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_show_staff_every_status_unless_they_filter()
    {
        await ListAsync(Query(staff: true));
        var unfiltered = _products.LastCriteria;
        await ListAsync(Query(staff: true, status: ProductStatusView.Draft));
        var draft = _products.LastCriteria;

        unfiltered.ShouldNotBeNull().Status.ShouldBeNull();
        draft.ShouldNotBeNull().Status.ShouldBe(ProductStatus.Draft);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Active")]
    public async Task Should_show_the_public_active_products_only(string? requested)
    {
        var status = requested is null ? (ProductStatusView?)null : Enum.Parse<ProductStatusView>(requested);

        await ListAsync(Query(staff: false, status: status));

        _products.LastCriteria.ShouldNotBeNull().Status.ShouldBe(ProductStatus.Active);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Discontinued")]
    public async Task Should_answer_the_public_an_empty_page_without_querying_when_they_ask_for_another_status(
        string requested
    )
    {
        SeedListing(3);

        var result = await ListAsync(Query(staff: false, status: Enum.Parse<ProductStatusView>(requested)));

        result.Value.Items.ShouldBeEmpty();
        result.Value.HasMore.ShouldBeFalse();
        _products.LastCriteria.ShouldBeNull();
    }

    [Theory]
    [InlineData("  cafeteira ", "cafeteira")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task Should_trim_the_search_text_and_ignore_a_blank_one(string? search, string? expected)
    {
        await ListAsync(Query(search: search));

        _products.LastCriteria.ShouldNotBeNull().SearchText.ShouldBe(expected);
    }

    [Fact]
    public async Task Should_summarise_each_product_with_its_price_and_status()
    {
        SeedListing(1);

        var result = await ListAsync(Query());

        var item = result.Value.Items.ShouldHaveSingleItem();
        item.Sku.ShouldBe("SKU-0001");
        item.Name.ShouldBe("Produto 01");
        item.PriceAmount.ShouldBe(10m);
        item.PriceCurrency.ShouldBe("BRL");
        item.Status.ShouldBe(ProductStatusView.Draft);
    }
}
