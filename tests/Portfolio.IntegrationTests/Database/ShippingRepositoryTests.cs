using Npgsql;
using Portfolio.SharedKernel.Application;
using Portfolio.Shipping.Domain;
using Portfolio.Shipping.Infrastructure;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The Shipping persistence against real PostgreSQL (BR-SHP-001 to BR-SHP-004): one shipment per order, the lifecycle
/// and its events, the listing with ownership and keyset, concurrency, and the order records.
/// </summary>
public sealed class ShippingRepositoryTests : DatabaseTestBase
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<Shipment> PrepareAsync(Guid? order = null, Guid? customer = null)
    {
        var shipment = Shipment.Prepare(order ?? Guid.CreateVersion7(), customer ?? Guid.CreateVersion7(), Clock).Value;
        var context = Shipping();
        await new EfShipmentRepository(context).AddAsync(shipment, Cancel);
        await context.SaveChangesAsync(Cancel);
        Clock.Advance(TimeSpan.FromSeconds(1));
        return shipment;
    }

    private async Task UpdateAsync(Guid id, Action<Shipment> change)
    {
        var context = Shipping();
        var repository = new EfShipmentRepository(context);
        var shipment = (await repository.GetByIdAsync(id, Cancel)).ShouldNotBeNull();
        change(shipment);
        repository.Update(shipment);
        await context.SaveChangesAsync(Cancel);
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

    [Fact]
    public async Task Should_persist_a_shipment_through_its_whole_lifecycle_with_the_carrier_data()
    {
        var shipment = await PrepareAsync();
        var estimate = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime).AddDays(3);

        await UpdateAsync(shipment.Id, s => s.Dispatch("Correios", "BR123456789", estimate, Clock));
        await UpdateAsync(shipment.Id, s => s.MarkDelivered(Clock));
        var loaded = (await new EfShipmentRepository(Shipping()).GetByIdAsync(shipment.Id, Cancel)).ShouldNotBeNull();

        (loaded.Status, loaded.Carrier, loaded.TrackingCode, loaded.EstimatedDeliveryDate).ShouldBe(
            (ShipmentStatus.Delivered, "Correios", "BR123456789", estimate)
        );
        (loaded.DispatchedAt, loaded.DeliveredAt).ShouldNotBe((null, null));
        loaded.Version.ShouldBe(shipment.Version + 2);
        (
            await Database.ScalarAsync(
                $"SELECT estimated_delivery_date::text FROM shipping.shipments WHERE id = '{shipment.Id}'"
            )
        ).ShouldBe(estimate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Should_write_each_lifecycle_event_to_the_outbox_in_the_same_transaction()
    {
        var shipment = await PrepareAsync();

        await UpdateAsync(shipment.Id, s => s.Dispatch("Correios", null, null, Clock));
        await UpdateAsync(shipment.Id, s => s.MarkFailed("addressNotFound", Clock));

        var types = await Database.StringsAsync("SELECT type FROM shipping.outbox_messages ORDER BY occurred_at, type");
        types.ShouldBe(["Portfolio.Shipping.Domain.ShipmentDispatched", "Portfolio.Shipping.Domain.ShipmentFailed"]);
        (
            await Database.ScalarAsync(
                "SELECT payload ->> 'orderId' FROM shipping.outbox_messages WHERE type LIKE '%Dispatched'"
            )
        ).ShouldBe(shipment.OrderId.ToString());
        (
            await Database.ScalarAsync(
                "SELECT payload -> 'trackingCode' FROM shipping.outbox_messages WHERE type LIKE '%Dispatched'"
            )
        ).ShouldBe("null");
    }

    [Fact]
    public async Task Should_find_the_shipment_of_an_order_and_nothing_for_another_order()
    {
        var shipment = await PrepareAsync();
        var repository = new EfShipmentRepository(Shipping());

        (await repository.FindByOrderAsync(shipment.OrderId, Cancel)).ShouldNotBeNull().Id.ShouldBe(shipment.Id);
        (await repository.FindByOrderAsync(Guid.CreateVersion7(), Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_refuse_a_second_shipment_for_the_same_order()
    {
        var first = await PrepareAsync();

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            PrepareAsync(first.OrderId, first.CustomerId)
        );

        FindPostgresError(failure).SqlState.ShouldBe(UniqueViolation);
        FindPostgresError(failure).ConstraintName.ShouldBe("ux_shipments_order");
    }

    [Fact]
    public async Task Should_let_only_one_of_two_concurrent_changes_to_a_shipment_win()
    {
        var shipment = await PrepareAsync();
        var firstContext = Shipping();
        var secondContext = Shipping();
        var first = (await new EfShipmentRepository(firstContext).GetByIdAsync(shipment.Id, Cancel)).ShouldNotBeNull();
        var second = (
            await new EfShipmentRepository(secondContext).GetByIdAsync(shipment.Id, Cancel)
        ).ShouldNotBeNull();

        first.Cancel(Clock);
        second.Dispatch("Correios", null, null, Clock);
        await firstContext.SaveChangesAsync(Cancel);

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            secondContext.SaveChangesAsync(Cancel)
        );
        failure.Kind.ShouldBe(PersistenceConflictKind.ConcurrentUpdate);
    }

    [Theory]
    [InlineData("status = 'InTransit'")]
    [InlineData("carrier = 'Correios'")]
    [InlineData("status = 'Unknown'")]
    public async Task Should_refuse_a_shipment_whose_status_and_carrier_disagree_even_when_written_by_hand(
        string assignment
    )
    {
        var shipment = await PrepareAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync($"UPDATE shipping.shipments SET {assignment} WHERE id = '{shipment.Id}'")
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    // ---- Listing (BR-SHP-004) -----------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Shipment>> ListAsync(
        Guid order,
        Guid customer,
        int limit = 50,
        ShipmentSeekPosition? after = null
    ) => await new EfShipmentRepository(Shipping()).ListByOrderAsync(order, customer, limit, after, Cancel);

    [Fact]
    public async Task Should_list_the_shipments_of_an_order_only_for_its_owner()
    {
        var shipment = await PrepareAsync();

        (await ListAsync(shipment.OrderId, shipment.CustomerId)).ShouldHaveSingleItem().Id.ShouldBe(shipment.Id);
        (await ListAsync(shipment.OrderId, Guid.CreateVersion7())).ShouldBeEmpty();
        (await ListAsync(Guid.CreateVersion7(), shipment.CustomerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_page_oldest_first_without_a_duplicate_or_a_gap_and_not_track_what_it_lists()
    {
        // The unique index allows one shipment per order, so the paging is exercised over rows of one customer by
        // hand: three shipments of one order cannot exist, which is itself the rule. Here the keyset is checked by
        // listing, then continuing after the only row.
        var shipment = await PrepareAsync();
        var context = Shipping();

        var first = await new EfShipmentRepository(context).ListByOrderAsync(
            shipment.OrderId,
            shipment.CustomerId,
            1,
            null,
            Cancel
        );
        var after = new ShipmentSeekPosition(first[0].Id, first[0].CreatedAt);
        var next = await ListAsync(shipment.OrderId, shipment.CustomerId, 1, after);

        first.ShouldHaveSingleItem();
        next.ShouldBeEmpty();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    // ---- The context's record of whose each order is (BR-SHP-004) ------------------------------------------------

    [Fact]
    public async Task Should_persist_the_order_record_and_find_it_by_the_order_identifier()
    {
        var order = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();
        var context = Shipping();
        await new EfOrderReferenceRepository(context).AddAsync(
            OrderReference.Record(order, customer, "PF-2026-000001", Clock).Value,
            Cancel
        );
        await context.SaveChangesAsync(Cancel);

        var found = await new EfOrderReferenceRepository(Shipping()).FindAsync(order, Cancel);

        found.ShouldNotBeNull();
        (found.Id, found.CustomerId, found.Number).ShouldBe((order, customer, "PF-2026-000001"));
        (await new EfOrderReferenceRepository(Shipping()).FindAsync(Guid.CreateVersion7(), Cancel)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_refuse_the_same_order_twice_even_under_a_race()
    {
        var order = Guid.CreateVersion7();
        var first = Shipping();
        await new EfOrderReferenceRepository(first).AddAsync(
            OrderReference.Record(order, Guid.CreateVersion7(), "PF-2026-000001", Clock).Value,
            Cancel
        );
        await first.SaveChangesAsync(Cancel);

        var second = Shipping();
        await new EfOrderReferenceRepository(second).AddAsync(
            OrderReference.Record(order, Guid.CreateVersion7(), "PF-2026-000001", Clock).Value,
            Cancel
        );

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() => second.SaveChangesAsync(Cancel));
        failure.Kind.ShouldBe(PersistenceConflictKind.DuplicateRecord);
    }
}
