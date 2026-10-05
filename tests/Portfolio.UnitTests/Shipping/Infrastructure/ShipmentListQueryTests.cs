using Microsoft.EntityFrameworkCore;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Domain;
using Portfolio.Shipping.Infrastructure;

namespace Portfolio.UnitTests.Shipping.Infrastructure;

/// <summary>
/// The SQL of the shipments listing (BR-SHP-004). Translating a query needs the provider but never a connection, so the
/// ownership filter and the keyset are checked here. Ownership is the one that must never regress.
/// </summary>
[Trait("Rule", "BR-SHP-004")]
public sealed class ShipmentListQueryTests : IDisposable
{
    private static readonly Guid Order = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f03");
    private static readonly Guid Customer = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f02");
    private static readonly Guid LastId = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01");

    private readonly ShippingDbContext _context = new(
        DesignTimeOptions.Create<ShippingDbContext>(ShippingDbContext.SchemaName)
    );

    public void Dispose() => _context.Dispose();

    private string Sql(int limit = 21, ShipmentSeekPosition? after = null) =>
        new EfShipmentRepository(_context).BuildListQuery(Order, Customer, limit, after).ToQueryString();

    [Fact]
    public void Should_bound_every_listing_to_the_order_and_to_its_owner_and_exclude_deleted_shipments()
    {
        var sql = Sql();

        sql.ShouldContain("s.order_id = @orderId");
        sql.ShouldContain("s.customer_id = @customerId");
        sql.ShouldContain($"-- @orderId='{Order}'");
        sql.ShouldContain($"-- @customerId='{Customer}'");
        sql.ShouldContain("s.deleted_at IS NULL");
    }

    [Fact]
    public void Should_order_oldest_first_with_the_identifier_as_tie_breaker_and_cap_the_page()
    {
        var sql = Sql(limit: 21);

        sql.ShouldContain("ORDER BY s.created_at, s.id");
        sql.ShouldContain("-- @p='21'");
        sql.ShouldContain("LIMIT @p");
    }

    [Fact]
    public void Should_continue_strictly_after_the_last_shipment_of_the_previous_page()
    {
        var after = new ShipmentSeekPosition(LastId, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var sql = Sql(after: after);

        sql.ShouldContain("(s.created_at > @createdAt OR (s.created_at = @createdAt AND s.id > @");
        sql.ShouldContain("-- @createdAt='2026-10-01T12:00:00.0000000+00:00'");
        sql.ShouldContain("s.customer_id = @customerId");
    }
}
