using Npgsql;
using Portfolio.Ordering.Domain;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The Ordering persistence against real PostgreSQL (BR-ORD-001 to BR-ORD-006): orders with their price snapshot, the
/// order number sequence, the unique indexes, the listing with ownership and keyset, concurrency, and the events that
/// reach the outbox in the same transaction.
/// </summary>
public sealed class OrderingRepositoryTests : DatabaseTestBase
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static OrderLine Line(string sku = "CAF-600-PRT", int quantity = 1, decimal price = 100m) =>
        new(Guid.CreateVersion7(), sku, "Cafeteira Elétrica", quantity, new Money(price, "BRL"));

    private async Task<Order> PlaceAsync(
        Guid? customer = null,
        decimal price = 100m,
        int quantity = 1,
        Guid? checkout = null,
        bool advanceClock = true
    )
    {
        var context = Ordering();
        var repository = new EfOrderRepository(context);
        var number = await repository.NextNumberAsync(Clock.GetUtcNow(), Cancel);
        var order = Order
            .Place(
                checkout ?? Guid.CreateVersion7(),
                customer ?? Guid.CreateVersion7(),
                number,
                [Line(quantity: quantity, price: price)],
                Clock
            )
            .Value;
        await repository.AddAsync(order, Cancel);
        await context.SaveChangesAsync(Cancel);

        if (advanceClock)
        {
            // Distinct placement instants keep the default order deterministic.
            Clock.Advance(TimeSpan.FromSeconds(1));
        }

        return order;
    }

    private async Task<Order> ReloadAsync(Guid id) =>
        (await new EfOrderRepository(Ordering()).GetByIdAsync(id, Cancel)).ShouldNotBeNull();

    private async Task<IReadOnlyList<Order>> ListAsync(OrderListCriteria criteria) =>
        await new EfOrderRepository(Ordering()).ListAsync(criteria, Cancel);

    private static OrderListCriteria Criteria(
        Guid customer,
        OrderSortField field = OrderSortField.PlacedAt,
        bool descending = true,
        int limit = 50,
        OrderStatus? status = null,
        DateTimeOffset? from = null,
        OrderSeekPosition? after = null
    ) => new(customer, field, descending, limit, status, from, after);

    private static string[] Numbers(IEnumerable<Order> orders) => [.. orders.Select(order => order.Number)];

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

    // ---- Persistence and the price snapshot (BR-ORD-001, BR-ORD-002) -------------------------------------------

    [Fact]
    public async Task Should_persist_an_order_with_its_lines_and_load_it_back_with_the_price_snapshot()
    {
        var coffee = Line("CAF-600-PRT", 2, 189.90m);
        var grinder = Line("MOE-100-PRT", 1, 49.50m);
        var customer = Guid.CreateVersion7();
        var context = Ordering();
        var repository = new EfOrderRepository(context);
        var order = Order.Place(Guid.CreateVersion7(), customer, "PF-2026-000001", [coffee, grinder], Clock).Value;
        await repository.AddAsync(order, Cancel);
        await context.SaveChangesAsync(Cancel);

        var loaded = await ReloadAsync(order.Id);

        (loaded.Number, loaded.CustomerId, loaded.Status, loaded.Version, loaded.Total).ShouldBe(
            ("PF-2026-000001", customer, OrderStatus.AwaitingPayment, order.Version, new Money(429.30m, "BRL"))
        );
        loaded.PlacedAt.ShouldBe(Clock.GetUtcNow());
        loaded.Items.Count.ShouldBe(2);
        var line = loaded.Items.Single(item => item.ProductId == coffee.ProductId);
        (line.Sku, line.Name, line.Quantity, line.UnitPrice, line.LineTotal).ShouldBe(
            ("CAF-600-PRT", "Cafeteira Elétrica", 2, new Money(189.90m, "BRL"), new Money(379.80m, "BRL"))
        );
        (loaded.PaidAt, loaded.CancelledAt, loaded.CancellationKey).ShouldBe((null, null, null));
    }

    [Fact]
    public async Task Should_write_the_placed_event_to_the_outbox_in_the_same_transaction_as_the_order()
    {
        var order = await PlaceAsync(price: 189.90m, quantity: 2);

        (await Database.ScalarAsync("SELECT count(*) FROM ordering.outbox_messages")).ShouldBe(1L);
        (
            await Database.ScalarAsync($"SELECT type FROM ordering.outbox_messages WHERE aggregate_id = '{order.Id}'")
        ).ShouldBe("Portfolio.Ordering.Domain.OrderPlaced");
        (await Database.ScalarAsync("SELECT payload ->> 'number' FROM ordering.outbox_messages")).ShouldBe(
            order.Number
        );
        (await Database.ScalarAsync("SELECT payload -> 'total' ->> 'amount' FROM ordering.outbox_messages")).ShouldBe(
            "379.80"
        );
    }

    [Fact]
    public async Task Should_find_the_order_placed_from_a_checkout_and_nothing_for_another_checkout()
    {
        var checkout = Guid.CreateVersion7();
        var order = await PlaceAsync(checkout: checkout);
        var repository = new EfOrderRepository(Ordering());

        var found = await repository.FindByCheckoutAsync(checkout, Cancel);
        var other = await repository.FindByCheckoutAsync(Guid.CreateVersion7(), Cancel);

        found.ShouldNotBeNull().Id.ShouldBe(order.Id);
        found.Items.Count.ShouldBe(1);
        other.ShouldBeNull();
    }

    [Fact]
    public async Task Should_refuse_a_second_order_for_the_same_checkout_even_when_two_placements_race()
    {
        var checkout = Guid.CreateVersion7();
        await PlaceAsync(checkout: checkout);

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() => PlaceAsync(checkout: checkout));

        FindPostgresError(failure).SqlState.ShouldBe(UniqueViolation);
        FindPostgresError(failure).ConstraintName.ShouldBe("ux_orders_checkout");
    }

    // ---- The order number (BR-ORD-005) --------------------------------------------------------------------------

    [Fact]
    public async Task Should_draw_increasing_numbers_with_the_year_of_the_placement()
    {
        var repository = new EfOrderRepository(Ordering());

        var first = await repository.NextNumberAsync(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero), Cancel);
        var second = await repository.NextNumberAsync(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), Cancel);

        (first, second).ShouldBe(("PF-2026-000001", "PF-2027-000002"));
    }

    [Fact]
    public async Task Should_never_give_two_concurrent_placements_the_same_number()
    {
        var drawn = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ => new EfOrderRepository(Ordering()).NextNumberAsync(Clock.GetUtcNow(), Cancel))
        );

        drawn.Distinct(StringComparer.Ordinal).Count().ShouldBe(20);
    }

    [Fact]
    public async Task Should_refuse_a_duplicate_order_number_even_when_written_by_hand()
    {
        var order = await PlaceAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync(
                $"""
                INSERT INTO ordering.orders (id, number, checkout_id, customer_id, status, placed_at, total_amount, total_currency, created_at, updated_at, version)
                VALUES ('{Guid.CreateVersion7()}', '{order.Number}', '{Guid.CreateVersion7()}', '{Guid.CreateVersion7()}', 'AwaitingPayment', now(), 10, 'BRL', now(), now(), 1)
                """
            )
        );

        failure.SqlState.ShouldBe(UniqueViolation);
    }

    // ---- Lifecycle persistence (BR-ORD-003, BR-ORD-004) --------------------------------------------------------

    [Fact]
    public async Task Should_persist_a_cancellation_with_its_reason_key_and_event_and_keep_the_note_out_of_the_event()
    {
        var order = await PlaceAsync();
        var context = Ordering();
        var repository = new EfOrderRepository(context);
        var loaded = (await repository.GetByIdAsync(order.Id, Cancel)).ShouldNotBeNull();
        loaded.MarkPaid(Clock);
        loaded.Cancel("changedMind", "dados pessoais aqui", "key-0001-aaaa", Clock).IsSuccess.ShouldBeTrue();
        repository.Update(loaded);
        await context.SaveChangesAsync(Cancel);

        var reloaded = await ReloadAsync(order.Id);

        (reloaded.Status, reloaded.CancellationReason, reloaded.CancellationNote, reloaded.CancellationKey).ShouldBe(
            (OrderStatus.Cancelled, "changedMind", "dados pessoais aqui", "key-0001-aaaa")
        );
        reloaded.CancelledAt.ShouldNotBeNull();
        reloaded.PaidAt.ShouldNotBeNull();
        reloaded.Version.ShouldBe(order.Version + 2);
        (await Database.ScalarAsync("SELECT count(*) FROM ordering.outbox_messages")).ShouldBe(3L);
        (
            await Database.ScalarAsync(
                "SELECT payload ->> 'wasPaid' FROM ordering.outbox_messages WHERE type LIKE '%OrderCancelled'"
            )
        ).ShouldBe("true");
        (
            await Database.ScalarAsync(
                "SELECT count(*) FROM ordering.outbox_messages WHERE payload::text LIKE '%dados pessoais%'"
            )
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_let_only_one_of_two_concurrent_changes_to_an_order_win()
    {
        var order = await PlaceAsync();
        var firstContext = Ordering();
        var secondContext = Ordering();
        var first = (await new EfOrderRepository(firstContext).GetByIdAsync(order.Id, Cancel)).ShouldNotBeNull();
        var second = (await new EfOrderRepository(secondContext).GetByIdAsync(order.Id, Cancel)).ShouldNotBeNull();

        first.Cancel("changedMind", null, "key-0001-aaaa", Clock);
        second.MarkPaid(Clock);
        await firstContext.SaveChangesAsync(Cancel);

        var failure = await Should.ThrowAsync<PersistenceConflictException>(() =>
            secondContext.SaveChangesAsync(Cancel)
        );
        failure.Kind.ShouldBe(PersistenceConflictKind.ConcurrentUpdate);
        (await ReloadAsync(order.Id)).Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Should_refuse_a_cancelled_status_without_its_cancellation_data_even_when_written_by_hand()
    {
        var order = await PlaceAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync($"UPDATE ordering.orders SET status = 'Cancelled' WHERE id = '{order.Id}'")
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    [Theory]
    [InlineData("total_amount = 0")]
    [InlineData("status = 'Unknown'")]
    public async Task Should_refuse_an_order_that_breaks_a_domain_rule_even_when_written_by_hand(string assignment)
    {
        var order = await PlaceAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync($"UPDATE ordering.orders SET {assignment} WHERE id = '{order.Id}'")
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    [Theory]
    [InlineData("quantity = 0")]
    [InlineData("quantity = 100")]
    [InlineData("unit_price_amount = 0")]
    public async Task Should_refuse_a_line_that_breaks_a_domain_rule_even_when_written_by_hand(string assignment)
    {
        var order = await PlaceAsync();

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Database.ExecuteAsync($"UPDATE ordering.order_items SET {assignment} WHERE order_id = '{order.Id}'")
        );

        failure.SqlState.ShouldBe(CheckViolation);
    }

    // ---- Listing: ownership, order, keyset, filters (BR-ORD-006) -----------------------------------------------

    [Fact]
    public async Task Should_list_only_the_orders_of_the_customer_asking()
    {
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var first = await PlaceAsync(mine);
        await PlaceAsync(theirs);
        var third = await PlaceAsync(mine);

        var listed = await ListAsync(Criteria(mine));

        Numbers(listed).ShouldBe([third.Number, first.Number]);
        (await ListAsync(Criteria(Guid.CreateVersion7()))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_list_newest_first_by_default_and_oldest_first_when_ascending()
    {
        var customer = Guid.CreateVersion7();
        var first = await PlaceAsync(customer);
        var second = await PlaceAsync(customer);
        var third = await PlaceAsync(customer);

        Numbers(await ListAsync(Criteria(customer))).ShouldBe([third.Number, second.Number, first.Number]);
        Numbers(await ListAsync(Criteria(customer, descending: false)))
            .ShouldBe([first.Number, second.Number, third.Number]);
    }

    [Fact]
    public async Task Should_order_by_total_in_both_directions()
    {
        var customer = Guid.CreateVersion7();
        var cheap = await PlaceAsync(customer, price: 10m);
        var dear = await PlaceAsync(customer, price: 300m);
        var middle = await PlaceAsync(customer, price: 100m);

        Numbers(await ListAsync(Criteria(customer, OrderSortField.Total, descending: false)))
            .ShouldBe([cheap.Number, middle.Number, dear.Number]);
        Numbers(await ListAsync(Criteria(customer, OrderSortField.Total, descending: true)))
            .ShouldBe([dear.Number, middle.Number, cheap.Number]);
    }

    [Theory]
    [InlineData("PlacedAt", true)]
    [InlineData("PlacedAt", false)]
    [InlineData("Total", true)]
    [InlineData("Total", false)]
    public async Task Should_walk_every_page_in_order_without_a_duplicate_or_a_gap_even_when_sort_values_tie(
        string fieldName,
        bool descending
    )
    {
        var field = Enum.Parse<OrderSortField>(fieldName);
        var customer = Guid.CreateVersion7();

        // Pairs of orders share the same total and, with a frozen clock, the same placement instant.
        foreach (var index in Enumerable.Range(1, 7))
        {
            await PlaceAsync(customer, price: 10m * ((index / 2) + 1), advanceClock: index % 2 == 0);
        }

        var everything = Numbers(await ListAsync(Criteria(customer, field, descending)));
        var walked = new List<string>();
        OrderSeekPosition? after = null;
        for (var page = 0; page < 10; page++)
        {
            var found = await ListAsync(Criteria(customer, field, descending, limit: 3, after: after));
            if (found.Count == 0)
            {
                break;
            }

            walked.AddRange(Numbers(found));
            after =
                field == OrderSortField.Total
                    ? new OrderSeekPosition(found[^1].Id, null, found[^1].Total.Amount)
                    : new OrderSeekPosition(found[^1].Id, found[^1].PlacedAt, null);
        }

        walked.ShouldBe(everything);
        walked.Distinct(StringComparer.Ordinal).Count().ShouldBe(7);
    }

    [Fact]
    public async Task Should_filter_by_status_and_by_the_start_of_the_period()
    {
        var customer = Guid.CreateVersion7();
        var old = await PlaceAsync(customer);
        Clock.Advance(TimeSpan.FromDays(2));
        var recent = await PlaceAsync(customer);
        var context = Ordering();
        var repository = new EfOrderRepository(context);
        var paid = (await repository.GetByIdAsync(recent.Id, Cancel)).ShouldNotBeNull();
        paid.MarkPaid(Clock);
        await context.SaveChangesAsync(Cancel);

        Numbers(await ListAsync(Criteria(customer, status: OrderStatus.Paid))).ShouldBe([recent.Number]);
        Numbers(await ListAsync(Criteria(customer, status: OrderStatus.AwaitingPayment))).ShouldBe([old.Number]);
        Numbers(await ListAsync(Criteria(customer, from: old.PlacedAt.AddDays(1)))).ShouldBe([recent.Number]);
        Numbers(await ListAsync(Criteria(customer, from: old.PlacedAt))).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Should_hide_a_deleted_order_from_every_query_and_keep_its_row()
    {
        var customer = Guid.CreateVersion7();
        var order = await PlaceAsync(customer);
        var context = Ordering();
        var repository = new EfOrderRepository(context);
        repository.Remove((await repository.GetByIdAsync(order.Id, Cancel)).ShouldNotBeNull());
        await context.SaveChangesAsync(Cancel);

        var reader = new EfOrderRepository(Ordering());
        (await reader.GetByIdAsync(order.Id, Cancel)).ShouldBeNull();
        (await reader.FindByCheckoutAsync(order.CheckoutId, Cancel)).ShouldBeNull();
        (await ListAsync(Criteria(customer))).ShouldBeEmpty();
        (await Database.ScalarAsync($"SELECT count(*) FROM ordering.orders WHERE id = '{order.Id}'")).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_list_without_tracking_and_without_loading_the_lines()
    {
        var customer = Guid.CreateVersion7();
        await PlaceAsync(customer);
        var context = Ordering();

        var listed = await new EfOrderRepository(context).ListAsync(Criteria(customer), Cancel);

        listed.ShouldHaveSingleItem().Items.ShouldBeEmpty();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }
}
