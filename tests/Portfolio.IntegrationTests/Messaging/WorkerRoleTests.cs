using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Portfolio.Catalog.Application;
using Portfolio.IntegrationTests.Http;
using Portfolio.SharedKernel.Infrastructure;
using RabbitMQ.Client;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// The whole application in its two roles (ai/CONTAINERS.md §6.1), composed by <c>Program</c> from configuration alone:
/// the API role never touches the broker, and the worker role carries a state change from one context to another
/// through the outbox, RabbitMQ and a consumer.
/// </summary>
public sealed class WorkerRoleTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static ApiFactory Worker(string suffix, string brokerConnectionString) =>
        new(
            "Production",
            configure: builder =>
            {
                // UseSetting, not ConfigureAppConfiguration: Program reads the role flags while it composes the services,
                // before the factory's configuration callbacks are applied.
                builder.UseSetting("Outbox:Relay:Enabled", "true");
                builder.UseSetting("Outbox:Relay:PollInterval", "00:00:00.200");
                builder.UseSetting("RabbitMq:ConsumersEnabled", "true");
                builder.UseSetting("RabbitMq:RetryBackoff", "00:00:00.050");
                builder.UseSetting("RabbitMq:Exchange", $"test-worker-{suffix}");
                builder.UseSetting("RabbitMq:DeadLetterExchange", $"test-worker-{suffix}.dead");
                builder.UseSetting("ConnectionStrings:RabbitMQ", brokerConnectionString);
            }
        );

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, Cancel);
        }

        throw new TimeoutException("The condition did not become true in time.");
    }

    [Fact]
    public async Task Should_open_the_inventory_item_of_a_product_created_in_the_catalog()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var factory = Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient(); // starts the host: the relays and the consumers begin

        using (var scope = factory.Services.CreateScope())
        {
            var created = await scope
                .ServiceProvider.GetRequiredService<CreateProductHandler>()
                .HandleAsync(
                    new CreateProductCommand("Cafeteira Elétrica 600ml", "CAF-600-PRT", 189.90m, "BRL"),
                    Cancel
                );
            created.IsSuccess.ShouldBeTrue();
        }

        await WaitUntilAsync(async () =>
            Equals(await factory.Database.ScalarAsync("SELECT count(*) FROM inventory.inventory_items"), 1L)
        );
        (await factory.Database.StringsAsync("SELECT sku FROM inventory.inventory_items"))
            .ShouldHaveSingleItem()
            .ShouldBe("CAF-600-PRT");
        (
            await factory.Database.ScalarAsync(
                "SELECT count(*) FROM catalog.outbox_messages WHERE processed_at IS NOT NULL"
            )
        ).ShouldBe(1L);
        (await factory.Database.ScalarAsync("SELECT count(*) FROM inventory.inbox_messages")).ShouldBe(1L);

        await CleanUpAsync(suffix);
    }

    [Fact]
    public async Task Should_still_start_and_serve_probes_when_the_broker_is_down_and_keep_events_in_the_outbox()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var factory = Worker(suffix, RabbitMqBroker.Unreachable);
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            (
                await scope
                    .ServiceProvider.GetRequiredService<CreateProductHandler>()
                    .HandleAsync(
                        new CreateProductCommand("Cafeteira Elétrica 600ml", "CAF-600-PRT", 189.90m, "BRL"),
                        Cancel
                    )
            ).IsSuccess.ShouldBeTrue();
        }

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Cancel);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WaitUntilAsync(async () =>
            Convert.ToInt64(
                await factory.Database.ScalarAsync("SELECT max(attempts) FROM catalog.outbox_messages"),
                System.Globalization.CultureInfo.InvariantCulture
            ) >= 1
        );
        (
            await factory.Database.ScalarAsync(
                "SELECT count(*) FROM catalog.outbox_messages WHERE processed_at IS NULL"
            )
        ).ShouldBe(1L);
        (await factory.Database.ScalarAsync("SELECT count(*) FROM inventory.inventory_items")).ShouldBe(0L);

        var health = factory.Services.GetRequiredService<HealthCheckService>();
        var report = await health.CheckHealthAsync(check => check.Name == RabbitMqHealthCheck.Name, Cancel);
        report.Entries[RabbitMqHealthCheck.Name].Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Should_not_register_the_broker_at_all_in_the_api_role()
    {
        using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        factory.Services.GetService<RabbitMqConnection>().ShouldBeNull();
        factory.Services.GetServices<IHostedService>().ShouldNotContain(service => service is RabbitMqConsumerService);
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task CleanUpAsync(string suffix)
    {
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqFixture.Current.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: CancellationToken.None);
        await channel.QueueDeleteAsync(
            "inventory.open-item-on-product-created",
            cancellationToken: CancellationToken.None
        );
        await channel.QueueDeleteAsync(
            "inventory.open-item-on-product-created.dead",
            cancellationToken: CancellationToken.None
        );
        await channel.QueueDeleteAsync(
            "customers.create-profile-on-customer-registered",
            cancellationToken: CancellationToken.None
        );
        await channel.QueueDeleteAsync(
            "customers.create-profile-on-customer-registered.dead",
            cancellationToken: CancellationToken.None
        );
        await channel.ExchangeDeleteAsync($"test-worker-{suffix}", cancellationToken: CancellationToken.None);
        await channel.ExchangeDeleteAsync($"test-worker-{suffix}.dead", cancellationToken: CancellationToken.None);
    }
}
