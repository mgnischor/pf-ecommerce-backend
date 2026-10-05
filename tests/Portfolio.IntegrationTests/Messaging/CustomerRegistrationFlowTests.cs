using System.Net.Http.Json;
using System.Text.Json;
using Portfolio.IntegrationTests.Http;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// A customer registers and, without anyone calling Customers, their profile appears: the API writes
/// <c>CustomerRegistered</c> to the Identity outbox, the relay publishes it to RabbitMQ, and the Customers consumer creates
/// the profile in its own schema (BR-CUS-006). The same application runs in the worker role next to the API, as in a
/// single-host deployment.
/// </summary>
public sealed class CustomerRegistrationFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Rule", "BR-CUS-006")]
    public async Task Should_create_the_profile_of_a_registered_customer_through_the_outbox_and_the_broker()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var cleanUp = new BrokerCleanUp(suffix);
        using var factory = WorkerRoleTests.Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient(); // starts the host: the relays and the consumers begin
        await WorkerRoleTests.WaitUntilConsumingAsync("customers.create-profile-on-customer-registered");
        var email = $"customer.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();

        using var created = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/registrations", UriKind.Relative),
            new
            {
                email,
                password,
                fullName = "Ana Souza",
                phone = "+5511987654321",
                locale = "en",
                timeZone = "Europe/Lisbon",
            },
            Cancel
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(await factory.Database.ScalarAsync("SELECT count(*) FROM customers.customer_profiles"), 1L)
        );

        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(client, email, password));
        using var profile = await AuthClient.GetAsync(client, "/api/v1/customers/me", tokens.AccessToken);
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("fullName").GetString().ShouldBe("Ana Souza");
        body.GetProperty("maskedEmail").GetString().ShouldBe("c***@example.com");
        body.GetProperty("maskedPhone").GetString().ShouldBe("+*********4321");
        body.GetProperty("locale").GetString().ShouldBe("en");
        body.GetProperty("timeZone").GetString().ShouldBe("Europe/Lisbon");

        // Both events of the registration were published, and only the Customers queue consumed the one with the data.
        (
            await factory.Database.ScalarAsync(
                "SELECT count(*) FROM identity.outbox_messages WHERE processed_at IS NOT NULL AND type LIKE '%CustomerRegistered'"
            )
        ).ShouldBe(1L);
        (await factory.Database.ScalarAsync("SELECT count(*) FROM customers.inbox_messages")).ShouldBe(1L);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-007")]
    public async Task Should_create_no_profile_for_an_account_an_administrator_creates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var cleanUp = new BrokerCleanUp(suffix);
        using var factory = WorkerRoleTests.Worker(suffix, RabbitMqFixture.Current.ConnectionString);
        using var client = factory.CreateClient();
        await WorkerRoleTests.WaitUntilConsumingAsync("customers.create-profile-on-customer-registered");
        var admin = await AuthClient.SignInAsync(client, factory.Accounts["administrator"]);

        using var created = await AuthClient.SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/users",
            admin.AccessToken,
            new
            {
                email = $"staff.{Guid.NewGuid():N}@example.com",
                password = ApiFactory.RandomPassword(),
                accessLevel = "collaborator",
            }
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The relay publishes the account events (they are processed), and none of them is addressed to Customers.
        await WorkerRoleTests.WaitUntilAsync(async () =>
            Equals(
                await factory.Database.ScalarAsync(
                    "SELECT count(*) FROM identity.outbox_messages WHERE processed_at IS NULL"
                ),
                0L
            )
        );
        (
            await factory.Database.ScalarAsync(
                "SELECT count(*) FROM identity.outbox_messages WHERE type LIKE '%CustomerRegistered'"
            )
        ).ShouldBe(0L);
        (await factory.Database.ScalarAsync("SELECT count(*) FROM customers.customer_profiles")).ShouldBe(0L);
    }
}
