using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Ordering.Application;
using Portfolio.Shipping.Application;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The fulfillment of an order across the Ordering and Shipping contexts, through the whole chain (BR-ORD-003, BR-SHP-001,
/// BR-SHP-002, BR-SHP-005): each state change is written to its own outbox in the same transaction, relayed to RabbitMQ,
/// and applied by the consumer of the other context in its own schema. Nothing here touches the other context's tables or types.
/// </summary>
public sealed class OrderFulfillmentFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Task WaitUntilConsumingAsync() =>
        WorkerRoleTests.WaitUntilConsumingAsync(
            "shipping.sync-orders",
            "ordering.mark-shipped-on-shipment-dispatched",
            "ordering.mark-delivered-on-shipment-delivered"
        );

    private static async Task<bool> StatusAsync(Http.ApiFactory factory, string table, Guid id, string expected) =>
        string.Equals(
            Convert.ToString(
                await factory.Database.ScalarAsync($"SELECT status FROM {table} WHERE id = '{id}'"),
                CultureInfo.InvariantCulture
            ),
            expected,
            StringComparison.Ordinal
        );

    private static async Task<Guid> ShipmentOfAsync(Http.ApiFactory factory, Guid order)
    {
        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(
                await factory.Database.ScalarAsync(
                    $"SELECT count(*) FROM shipping.shipments WHERE order_id = '{order}'"
                ),
                1L
            )
        );
        return (Guid)
            (await factory.Database.ScalarAsync($"SELECT id FROM shipping.shipments WHERE order_id = '{order}'"))!;
    }

    private static async Task<Guid> PlaceAsync(IServiceProvider provider, Guid customer) =>
        (
            await provider
                .GetRequiredService<PlaceOrderHandler>()
                .HandleAsync(
                    new PlaceOrderCommand(
                        Guid.CreateVersion7(),
                        customer,
                        [
                            new PlaceOrderLine(
                                Guid.CreateVersion7(),
                                "CAF-600-PRT",
                                "Cafeteira Elétrica",
                                2,
                                189.90m,
                                "BRL"
                            ),
                        ]
                    ),
                    Cancel
                )
        )
            .Value
            .Id;

    private static async Task PayAsync(IServiceProvider provider, Guid order) =>
        (
            await provider
                .GetRequiredService<AdvanceOrderHandler>()
                .HandleAsync(
                    new AdvanceOrderCommand(Guid.CreateVersion7(), "ordering.billing-test", order, OrderMilestone.Paid),
                    Cancel
                )
        ).IsSuccess.ShouldBeTrue();

    private static async Task DispatchAsync(IServiceProvider provider, Guid shipment) =>
        (
            await provider
                .GetRequiredService<DispatchShipmentHandler>()
                .HandleAsync(new DispatchShipmentCommand(shipment, "Correios", "BR123456789", null), Cancel)
        ).IsSuccess.ShouldBeTrue();

    private static async Task DeliverAsync(IServiceProvider provider, Guid shipment) =>
        (
            await provider
                .GetRequiredService<ConcludeShipmentHandler>()
                .HandleAsync(new ConcludeShipmentCommand(shipment, ShipmentOutcome.Delivered), Cancel)
        ).IsSuccess.ShouldBeTrue();

    // Every event of the chain was relayed, and each consumer recorded the messages it handled in its own inbox.
    private static async Task AssertEverythingRelayedAsync(Http.ApiFactory factory)
    {
        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(
                await factory.Database.ScalarAsync(
                    "SELECT count(*) FROM ordering.outbox_messages WHERE processed_at IS NULL"
                ),
                0L
            )
            && Equals(
                await factory.Database.ScalarAsync(
                    "SELECT count(*) FROM shipping.outbox_messages WHERE processed_at IS NULL"
                ),
                0L
            )
        );
        (await factory.Database.ScalarAsync("SELECT count(*) FROM shipping.inbox_messages")).ShouldBe(2L); // placed, paid
        (await factory.Database.ScalarAsync("SELECT count(*) FROM ordering.inbox_messages")).ShouldBe(3L); // paid (reported by the test), dispatched, delivered
    }

    [Fact]
    public async Task Should_carry_an_order_from_placement_to_delivery_across_both_contexts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var cleanUp = new BrokerCleanUp(suffix); // runs even when the test fails, so no queue is left behind
        using var factory = WorkerRoleTests.Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient(); // starts the host: the relays and the consumers begin
        await WaitUntilConsumingAsync();
        var customer = Guid.CreateVersion7();

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var order = await PlaceAsync(provider, customer);
        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(
                await factory.Database.ScalarAsync(
                    $"SELECT count(*) FROM shipping.order_references WHERE id = '{order}'"
                ),
                1L
            )
        );
        (
            await factory.Database.ScalarAsync(
                $"SELECT customer_id::text FROM shipping.order_references WHERE id = '{order}'"
            )
        ).ShouldBe(customer.ToString());
        (await factory.Database.ScalarAsync("SELECT count(*) FROM shipping.shipments")).ShouldBe(0L);

        // Payment is reported to Ordering by the context that will own it (Billing); the handler is the entry point.
        await PayAsync(provider, order);
        var shipment = await ShipmentOfAsync(factory, order);
        (await StatusAsync(factory, "shipping.shipments", shipment, "Preparing")).ShouldBeTrue();

        await DispatchAsync(provider, shipment);
        await WorkerRoleTests.WaitUntilAsync(async () =>
            await StatusAsync(factory, "ordering.orders", order, "Shipped")
        );

        await DeliverAsync(provider, shipment);
        await WorkerRoleTests.WaitUntilAsync(async () =>
            await StatusAsync(factory, "ordering.orders", order, "Delivered")
        );

        await AssertEverythingRelayedAsync(factory);
    }

    [Fact]
    public async Task Should_cancel_the_shipment_that_is_still_being_prepared_when_the_order_is_cancelled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var cleanUp = new BrokerCleanUp(suffix); // runs even when the test fails, so no queue is left behind
        using var factory = WorkerRoleTests.Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient();
        await WaitUntilConsumingAsync();
        var customer = Guid.CreateVersion7();

        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var order = await PlaceAsync(provider, customer);
        await PayAsync(provider, order);
        var shipment = await ShipmentOfAsync(factory, order);

        var cancelled = await provider
            .GetRequiredService<CancelOrderHandler>()
            .HandleAsync(new CancelOrderCommand(customer, order, "key-flow-0001", 2, "changedMind", null), Cancel);

        cancelled.IsSuccess.ShouldBeTrue();
        await WorkerRoleTests.WaitUntilAsync(async () =>
            await StatusAsync(factory, "shipping.shipments", shipment, "Cancelled")
        );
        (await StatusAsync(factory, "ordering.orders", order, "Cancelled")).ShouldBeTrue();
        (
            await factory.Database.ScalarAsync(
                $"SELECT count(*) FROM shipping.outbox_messages WHERE aggregate_id = '{shipment}' AND type LIKE '%ShipmentCancelled'"
            )
        ).ShouldBe(1L);
    }
}
