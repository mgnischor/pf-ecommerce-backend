using Microsoft.Extensions.DependencyInjection;
using Portfolio.Catalog.Application;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The Cart's view of the catalog through the whole chain (BR-CRT-005): a Catalog state change is written to the
/// Catalog's outbox in the same transaction, relayed to RabbitMQ, and applied by the Cart's consumer in its own schema.
/// Every Catalog event a cart depends on is carried: creation, activation, price change, and deletion.
/// </summary>
public sealed class CartCatalogFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<bool> ViewAsync(Http.ApiFactory factory, Guid id, string column, string expected) =>
        string.Equals(
            Convert.ToString(
                await factory.Database.ScalarAsync(
                    $"SELECT {column}::text FROM cart.catalog_products WHERE id = '{id}'"
                ),
                System.Globalization.CultureInfo.InvariantCulture
            ),
            expected,
            StringComparison.Ordinal
        );

    [Fact]
    public async Task Should_carry_every_catalog_change_of_a_product_into_the_carts_view()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var factory = WorkerRoleTests.Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient(); // starts the host: the relays and the consumers begin
        await WorkerRoleTests.WaitUntilConsumingAsync("cart.sync-catalog-products");

        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            id = (
                await provider
                    .GetRequiredService<CreateProductHandler>()
                    .HandleAsync(
                        new CreateProductCommand("Cafeteira Elétrica 600ml", "CAF-600-PRT", 189.90m, "BRL"),
                        Cancel
                    )
            )
                .Value
                .Id;
            await WorkerRoleTests.WaitUntilAsync(async () => await ViewAsync(factory, id, "sellable", "false"));
            (await ViewAsync(factory, id, "price_amount", "189.9000")).ShouldBeTrue();
            (await ViewAsync(factory, id, "sku", "CAF-600-PRT")).ShouldBeTrue();

            (
                await provider
                    .GetRequiredService<ActivateProductHandler>()
                    .HandleAsync(new ActivateProductCommand(id, 1), Cancel)
            ).IsSuccess.ShouldBeTrue();
            await WorkerRoleTests.WaitUntilAsync(async () => await ViewAsync(factory, id, "sellable", "true"));

            (
                await provider
                    .GetRequiredService<ChangeProductPriceHandler>()
                    .HandleAsync(new ChangeProductPriceCommand(id, 159.90m, "BRL", 2), Cancel)
            ).IsSuccess.ShouldBeTrue();
            await WorkerRoleTests.WaitUntilAsync(async () => await ViewAsync(factory, id, "price_amount", "159.9000"));

            (
                await provider
                    .GetRequiredService<DeleteProductHandler>()
                    .HandleAsync(new DeleteProductCommand(id, "key-deletion-0001", 3), Cancel)
            ).IsSuccess.ShouldBeTrue();
            await WorkerRoleTests.WaitUntilAsync(async () => await ViewAsync(factory, id, "sellable", "false"));
        }

        // One inbox row per event handled: created, activated, repriced, deleted.
        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(await factory.Database.ScalarAsync("SELECT count(*) FROM cart.inbox_messages"), 4L)
        );
        (await ViewAsync(factory, id, "status_version", "4")).ShouldBeTrue();
        (await ViewAsync(factory, id, "price_version", "3")).ShouldBeTrue();

        await WorkerRoleTests.CleanUpAsync(suffix);
    }
}
