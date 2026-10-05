using Microsoft.EntityFrameworkCore;
using Portfolio.Ordering.Domain;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.UnitTests.Ordering.Infrastructure;

/// <summary>
/// The SQL of an order listing (BR-ORD-006). Translating a query needs the provider but never a connection, so the
/// ownership filter, the keyset, and the ordering that would otherwise surface only against a real database are checked
/// here. Ownership is the one that must never regress: every listing is bounded to one customer.
/// </summary>
[Trait("Rule", "BR-ORD-006")]
public sealed class OrderListQueryTests : IDisposable
{
    private static readonly Guid Customer = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f02");
    private static readonly Guid LastId = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01");

    private readonly OrderingDbContext _context = new(
        DesignTimeOptions.Create<OrderingDbContext>(OrderingDbContext.SchemaName)
    );

    public void Dispose() => _context.Dispose();

    private string Sql(OrderListCriteria criteria) =>
        new EfOrderRepository(_context).BuildListQuery(criteria).ToQueryString();

    [Fact]
    public void Should_always_filter_by_the_owner_and_exclude_deleted_orders()
    {
        var sql = Sql(new OrderListCriteria(Customer, OrderSortField.PlacedAt, true, 21));

        sql.ShouldContain("o.customer_id = @customerId");
        sql.ShouldContain($"-- @customerId='{Customer}'");
        sql.ShouldContain("o.deleted_at IS NULL");
        sql.ShouldContain("ORDER BY o.placed_at DESC, o.id DESC");
        sql.ShouldContain("LIMIT @p");
    }

    [Theory]
    [InlineData("PlacedAt", true, "ORDER BY o.placed_at DESC, o.id DESC")]
    [InlineData("PlacedAt", false, "ORDER BY o.placed_at, o.id")]
    [InlineData("Total", true, "ORDER BY o.total_amount DESC, o.id DESC")]
    [InlineData("Total", false, "ORDER BY o.total_amount, o.id")]
    public void Should_order_by_the_requested_field_with_the_identifier_as_tie_breaker(
        string field,
        bool descending,
        string expected
    )
    {
        var sql = Sql(new OrderListCriteria(Customer, Enum.Parse<OrderSortField>(field), descending, 10));

        sql.ShouldContain(expected);
        sql.ShouldContain("o.customer_id = @customerId");
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_order_when_listing_by_placement()
    {
        var placedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var after = new OrderSeekPosition(LastId, placedAt, null);

        var sql = Sql(new OrderListCriteria(Customer, OrderSortField.PlacedAt, true, 10, After: after));

        sql.ShouldContain("(o.placed_at < @placedAt OR (o.placed_at = @placedAt AND o.id < @");
        sql.ShouldContain("-- @placedAt='2026-10-01T12:00:00.0000000+00:00'");
        sql.ShouldContain("o.customer_id = @customerId");
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_order_when_listing_by_total_ascending()
    {
        var after = new OrderSeekPosition(LastId, null, 379.80m);

        var sql = Sql(new OrderListCriteria(Customer, OrderSortField.Total, false, 10, After: after));

        sql.ShouldContain("(o.total_amount > @amount OR (o.total_amount = @amount AND o.id > @");
        sql.ShouldContain("-- @amount='379.80'");
    }

    [Fact]
    public void Should_filter_by_status_and_by_the_start_of_the_period()
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var sql = Sql(new OrderListCriteria(Customer, OrderSortField.PlacedAt, true, 10, OrderStatus.Paid, from));

        sql.ShouldContain("o.status = @status");
        sql.ShouldContain("-- @status='Paid'");
        sql.ShouldContain("o.placed_at >= @placedFrom");
        sql.ShouldContain("-- @placedFrom='2026-10-01T00:00:00.0000000+00:00'");
    }

    [Fact]
    public void Should_not_load_the_lines_of_the_orders_it_lists()
    {
        var sql = Sql(new OrderListCriteria(Customer, OrderSortField.PlacedAt, true, 10));

        sql.ShouldNotContain("order_items");
    }

    [Fact]
    public void Should_reject_a_seek_position_that_does_not_match_the_sort_field()
    {
        var after = new OrderSeekPosition(LastId, null, 10m);

        Should.Throw<ArgumentException>(() =>
            Sql(new OrderListCriteria(Customer, OrderSortField.PlacedAt, true, 10, After: after))
        );
    }
}
