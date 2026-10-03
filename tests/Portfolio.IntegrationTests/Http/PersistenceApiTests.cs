using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// What the API leaves in PostgreSQL, seen from the outside (ai/DATABASE.md §8.2): committed state, events written with
/// it, no secrets at rest, and races between real HTTP requests settled by the database rather than by luck.
/// </summary>
public sealed class PersistenceApiTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private string Manager => fixture.TokenFor("manager").AccessToken;

    private TestDatabaseView Db => new(fixture.Factory.Database);

    private static string NewEmail() => $"customer.{Guid.NewGuid():N}@example.com";

    private static string NewSku() => "P-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    private Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email, password }
        );

    private async Task<HttpResponseMessage> AdjustAsync(string sku, string ifMatch, string key, int delta)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/inventory/items/{sku}/adjustments", UriKind.Relative)
        )
        {
            Content = JsonContent.Create(new { delta, reasonCode = "stocktake" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Manager);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> OpenItemAsync()
    {
        var sku = NewSku();
        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            Manager,
            new { sku }
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return sku;
    }

    [Fact]
    public async Task Should_store_an_account_with_a_password_verifier_and_never_the_password()
    {
        var email = NewEmail();
        var password = ApiFactory.RandomPassword();

        using var created = await RegisterAsync(email, password);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var hash = await Db.StringAsync($"SELECT password_hash FROM identity.users WHERE email = '{email}'");
        hash.ShouldStartWith("$argon2id$");
        hash.ShouldNotContain(password);
        (await Db.StringAsync($"SELECT access_level FROM identity.users WHERE email = '{email}'")).ShouldBe("Public");
    }

    [Fact]
    public async Task Should_write_the_registration_event_in_the_same_commit_as_the_account()
    {
        var email = NewEmail();

        using var created = await RegisterAsync(email, ApiFactory.RandomPassword());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var types = await fixture.Factory.Database.StringsAsync(
            """
            SELECT o.type FROM identity.outbox_messages o
            JOIN identity.users u ON u.id = o.aggregate_id
            """ + $" WHERE u.email = '{email}'"
        );
        // A self-registration writes both events with the account, in one commit (BR-CUS-006).
        types.Count.ShouldBe(2);
        types.ShouldContain(type => type.EndsWith(".UserRegistered", StringComparison.Ordinal));
        types.ShouldContain(type => type.EndsWith(".CustomerRegistered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_not_leave_the_password_or_the_email_in_the_event_payload_beyond_what_the_event_declares()
    {
        var password = ApiFactory.RandomPassword();
        using var created = await RegisterAsync(NewEmail(), password);

        var payloads = await fixture.Factory.Database.StringsAsync(
            "SELECT payload::text FROM identity.outbox_messages"
        );

        payloads.ShouldAllBe(payload => !payload.Contains(password, StringComparison.Ordinal));
        payloads.ShouldAllBe(payload => !payload.Contains("argon2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_keep_the_email_out_of_the_event_every_consumer_of_accounts_receives()
    {
        var email = NewEmail();
        using var created = await RegisterAsync(email, ApiFactory.RandomPassword());

        var payloads = await fixture.Factory.Database.StringsAsync(
            "SELECT o.payload::text FROM identity.outbox_messages o JOIN identity.users u ON u.id = o.aggregate_id"
                + $" WHERE u.email = '{email}' AND o.type LIKE '%.UserRegistered'"
        );
        var customerEvent = await fixture.Factory.Database.StringsAsync(
            "SELECT o.payload::text FROM identity.outbox_messages o JOIN identity.users u ON u.id = o.aggregate_id"
                + $" WHERE u.email = '{email}' AND o.type LIKE '%.CustomerRegistered'"
        );

        payloads.ShouldHaveSingleItem().ShouldNotContain(email, Case.Insensitive);
        // The event that does carry the e-mail is the one only the Customers queue binds to.
        customerEvent.ShouldHaveSingleItem().ShouldContain(email);
    }

    [Fact]
    public async Task Should_let_exactly_one_of_two_simultaneous_registrations_of_the_same_email_win()
    {
        var email = NewEmail();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => RegisterAsync(email, ApiFactory.RandomPassword()))
        );

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        (await Db.StringAsync($"SELECT count(*) FROM identity.users WHERE email = '{email}'")).ShouldBe("1");
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Should_let_only_one_of_two_simultaneous_refreshes_of_the_same_token_succeed()
    {
        var email = NewEmail();
        var password = ApiFactory.RandomPassword();
        using var created = await RegisterAsync(email, password);
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken))
        );

        // Never two sessions out of one token, whichever way the race went.
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(1);
        responses.ShouldAllBe(response => response.StatusCode != HttpStatusCode.InternalServerError);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Should_commit_a_stock_adjustment_its_ledger_line_and_its_event_together()
    {
        var sku = await OpenItemAsync();

        using var response = await AdjustAsync(sku, "\"1\"", Guid.NewGuid().ToString(), 12);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Db.StringAsync($"SELECT on_hand FROM inventory.inventory_items WHERE sku = '{sku}'")).ShouldBe("12");
        (await Db.StringAsync($"SELECT version FROM inventory.inventory_items WHERE sku = '{sku}'")).ShouldBe("2");
        (
            await Db.StringAsync(
                $"""
                SELECT count(*) FROM inventory.stock_movements m
                JOIN inventory.inventory_items i ON i.id = m.inventory_item_id
                WHERE i.sku = '{sku}' AND m.delta = 12 AND m.reason_code = 'stocktake' AND m.recorded_by <> '00000000-0000-0000-0000-000000000000'
                """
            )
        ).ShouldBe("1");
        var events = await fixture.Factory.Database.StringsAsync(
            $"""
            SELECT split_part(o.type, '.', 4) FROM inventory.outbox_messages o
            JOIN inventory.inventory_items i ON i.id = o.aggregate_id
            WHERE i.sku = '{sku}' ORDER BY o.aggregate_version
            """
        );
        events.ShouldBe(["InventoryItemOpened", "StockAdjusted"]);
    }

    [Fact]
    public async Task Should_apply_a_retried_adjustment_once_in_the_database()
    {
        var sku = await OpenItemAsync();
        var key = Guid.NewGuid().ToString();

        using var first = await AdjustAsync(sku, "\"1\"", key, 5);
        using var retry = await AdjustAsync(sku, "\"1\"", key, 5);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Db.StringAsync($"SELECT on_hand FROM inventory.inventory_items WHERE sku = '{sku}'")).ShouldBe("5");
    }

    [Fact]
    public async Task Should_settle_simultaneous_adjustments_with_the_same_version_without_losing_or_doubling_stock()
    {
        var sku = await OpenItemAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => AdjustAsync(sku, "\"1\"", Guid.NewGuid().ToString(), 10))
        );

        var succeeded = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        succeeded.ShouldBe(1);
        responses
            .Where(response => response.StatusCode != HttpStatusCode.OK)
            .ShouldAllBe(response =>
                response.StatusCode == HttpStatusCode.Conflict
                || response.StatusCode == HttpStatusCode.PreconditionFailed
            );
        (await Db.StringAsync($"SELECT on_hand FROM inventory.inventory_items WHERE sku = '{sku}'")).ShouldBe("10");
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Should_answer_a_lost_commit_race_with_409_and_a_stable_code_and_no_database_detail()
    {
        var sku = await OpenItemAsync();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => AdjustAsync(sku, "\"1\"", Guid.NewGuid().ToString(), 1))
        );

        var conflicts = responses.Where(response => response.StatusCode == HttpStatusCode.Conflict).ToList();

        foreach (var conflict in conflicts)
        {
            using var body = JsonDocument.Parse(
                await conflict.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            body.RootElement.GetProperty("code").GetString().ShouldBe("CONCURRENT_UPDATE");
            body.RootElement.GetProperty("status").GetInt32().ShouldBe(409);
            body.RootElement.ToString().ShouldNotContain("inventory_items");
            body.RootElement.ToString().ShouldNotContain("Npgsql");
            conflict.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Should_report_not_ready_when_the_database_is_gone_and_ready_while_it_is_there()
    {
        using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        using var before = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken
        );
        factory.Database.Dispose();
        using var after = await client.GetAsync(
            new Uri("/health/ready", UriKind.Relative),
            TestContext.Current.CancellationToken
        );
        using var live = await client.GetAsync(
            new Uri("/health/live", UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await after.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Unhealthy");
        // Liveness never depends on a backing service: a database outage must not get the process restarted.
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class TestDatabaseView(Database.TestDatabase database)
    {
        public async Task<string> StringAsync(string sql) =>
            Convert.ToString(await database.ScalarAsync(sql), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }
}
