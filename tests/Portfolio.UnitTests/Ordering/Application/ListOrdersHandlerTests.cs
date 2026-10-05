using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.Ordering.Application;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.UnitTests.Ordering.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Ordering.Application;

[Trait("Rule", "BR-ORD-006")]
public sealed class ListOrdersHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderRepository _orders = new();
    private readonly HmacPageCursorCodec _cursors = new(
        Options.Create(
            new PageCursorOptions { CursorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }
        ),
        environmentAllowsEphemeralKey: false,
        NullLogger<HmacPageCursorCodec>.Instance
    );
    private readonly ListOrdersHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public ListOrdersHandlerTests()
    {
        _handler = new ListOrdersHandler(_orders, _cursors);
    }

    private ListOrdersQuery Query(
        int limit = 20,
        string? cursor = null,
        string? sort = null,
        OrderStatusView? status = null,
        DateOnly? createdFrom = null,
        Guid? customer = null
    ) => new(customer ?? _customer, limit, cursor, sort, status, createdFrom);

    private void SeedListing(int count)
    {
        for (var index = 1; index <= count; index++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _orders.ListResult.Add(
                OrderBuilder
                    .New()
                    .ForCustomer(_customer)
                    .Numbered($"PF-2026-{index:000000}")
                    .WithLines(OrderBuilder.Line($"SKU-{index:0000}", 1, 10m * index))
                    .Build(_clock)
            );
        }
    }

    private Task<Result<OrderPage>> ListAsync(ListOrdersQuery query) =>
        _handler.HandleAsync(query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_list_only_the_callers_orders_newest_first_asking_for_one_extra_row()
    {
        await ListAsync(Query());

        var criteria = _orders.LastCriteria.ShouldNotBeNull();
        criteria.CustomerId.ShouldBe(_customer);
        criteria.SortField.ShouldBe(OrderSortField.PlacedAt);
        criteria.Descending.ShouldBeTrue();
        criteria.Limit.ShouldBe(21);
        (criteria.Status, criteria.PlacedFrom, criteria.After).ShouldBe((null, null, null));
    }

    [Theory]
    [InlineData("placedAt", "PlacedAt", false)]
    [InlineData("-placedAt", "PlacedAt", true)]
    [InlineData("total", "Total", false)]
    [InlineData("-total", "Total", true)]
    public async Task Should_order_by_the_allowed_sort_field_and_direction(string sort, string field, bool descending)
    {
        await ListAsync(Query(sort: sort));

        var criteria = _orders.LastCriteria.ShouldNotBeNull();
        criteria.SortField.ShouldBe(Enum.Parse<OrderSortField>(field));
        criteria.Descending.ShouldBe(descending);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("-status")]
    [InlineData("PlacedAt")]
    [InlineData("customerId")]
    [InlineData("--total")]
    public async Task Should_reject_a_sort_field_outside_the_allowlist_as_a_malformed_request(string sort)
    {
        var result = await ListAsync(Query(sort: sort));

        result.ShouldFail().Code.ShouldBe("SORT_FIELD_NOT_ALLOWED");
        result.ShouldFail().Type.ShouldBe(ErrorType.BadRequest);
        _orders.LastCriteria.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(100, 101)]
    [InlineData(5000, 101)]
    public async Task Should_clamp_the_page_size_to_the_documented_range(int limit, int expectedRowsRequested)
    {
        await ListAsync(Query(limit: limit));

        _orders.LastCriteria.ShouldNotBeNull().Limit.ShouldBe(expectedRowsRequested);
    }

    [Fact]
    public async Task Should_pass_the_status_and_the_start_of_the_day_in_utc_as_filters()
    {
        await ListAsync(Query(status: OrderStatusView.Paid, createdFrom: new DateOnly(2026, 10, 1)));

        var criteria = _orders.LastCriteria.ShouldNotBeNull();
        criteria.Status.ShouldBe(OrderStatus.Paid);
        criteria.PlacedFrom.ShouldBe(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Should_answer_an_empty_page_without_a_cursor_when_nothing_matches()
    {
        var result = await ListAsync(Query());

        result.Value.Items.ShouldBeEmpty();
        (result.Value.HasMore, result.Value.NextCursor).ShouldBe((false, null));
    }

    [Fact]
    public async Task Should_trim_the_page_and_issue_a_cursor_when_more_rows_exist()
    {
        SeedListing(3);

        var result = await ListAsync(Query(limit: 2));

        result.Value.Items.Select(item => item.Number).ShouldBe(["PF-2026-000001", "PF-2026-000002"]);
        result.Value.HasMore.ShouldBeTrue();
        result.Value.NextCursor.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Should_answer_the_last_page_without_a_cursor_when_the_rows_fit_exactly()
    {
        SeedListing(2);

        var result = await ListAsync(Query(limit: 2));

        (result.Value.Items.Count, result.Value.HasMore, result.Value.NextCursor).ShouldBe((2, false, null));
    }

    [Theory]
    [InlineData("-placedAt")]
    [InlineData("total")]
    public async Task Should_continue_after_the_last_order_of_the_previous_page(string sort)
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2, sort: sort));
        var last = _orders.ListResult[1];

        await ListAsync(Query(limit: 2, sort: sort, cursor: first.Value.NextCursor));

        var after = _orders.LastCriteria.ShouldNotBeNull().After.ShouldNotBeNull();
        after.Id.ShouldBe(last.Id);
        after.PlacedAt.ShouldBe(sort == "-placedAt" ? last.PlacedAt : null);
        after.TotalAmount.ShouldBe(sort == "total" ? last.Total.Amount : null);
    }

    [Fact]
    public async Task Should_reject_a_forged_cursor_as_a_malformed_request()
    {
        var result = await ListAsync(Query(cursor: "bm90LWEtY3Vyc29y.AAAA"));

        result.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        result.ShouldFail().Type.ShouldBe(ErrorType.BadRequest);
        _orders.LastCriteria.ShouldBeNull();
    }

    [Fact]
    public async Task Should_never_honour_a_cursor_issued_to_another_customer()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2));

        var stolen = await ListAsync(Query(limit: 2, cursor: first.Value.NextCursor, customer: Guid.CreateVersion7()));

        stolen.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
    }

    [Fact]
    public async Task Should_reject_a_cursor_issued_for_another_sort_or_other_filters()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 2, sort: "total"));
        var cursor = first.Value.NextCursor;

        var otherSort = await ListAsync(Query(limit: 2, sort: "placedAt", cursor: cursor));
        var otherStatus = await ListAsync(Query(limit: 2, sort: "total", status: OrderStatusView.Paid, cursor: cursor));
        var otherDate = await ListAsync(
            Query(limit: 2, sort: "total", createdFrom: new DateOnly(2026, 1, 1), cursor: cursor)
        );

        otherSort.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        otherStatus.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        otherDate.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
    }

    [Fact]
    public async Task Should_accept_a_cursor_with_the_same_query_whatever_the_page_size()
    {
        SeedListing(3);
        var first = await ListAsync(Query(limit: 1));

        var next = await ListAsync(Query(limit: 2, cursor: first.Value.NextCursor));

        next.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_summarise_each_order_with_its_total_and_status()
    {
        SeedListing(1);

        var item = (await ListAsync(Query())).Value.Items.ShouldHaveSingleItem();

        (item.Number, item.Status, item.TotalAmount, item.Currency).ShouldBe(
            ("PF-2026-000001", OrderStatusView.AwaitingPayment, 10m, "BRL")
        );
    }
}
