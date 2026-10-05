using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Ordering.Application;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The Ordering use cases through the real pipeline and a real PostgreSQL (BR-ORD-001 to BR-ORD-006): reading and listing
/// only the caller's own orders, and cancelling with <c>If-Match</c> and <c>Idempotency-Key</c>. Orders are placed with the
/// handler the Checkout will call, since no endpoint places one.
/// </summary>
public sealed class OrdersTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private sealed record Customer(Guid Id, string Token);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<Customer> NewCustomerAsync()
    {
        var email = $"buyer.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email, password }
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var me = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        return new Customer(
            (await me.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid(),
            tokens.AccessToken
        );
    }

    private async Task<Guid> PlaceAsync(
        Customer customer,
        decimal price = 189.90m,
        int quantity = 2,
        string sku = "CAF-600-PRT"
    )
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<PlaceOrderHandler>()
            .HandleAsync(
                new PlaceOrderCommand(
                    Guid.CreateVersion7(),
                    customer.Id,
                    [new PlaceOrderLine(Guid.CreateVersion7(), sku, "Cafeteira Elétrica 600ml", quantity, price, "BRL")]
                ),
                Cancel
            );

        result.IsSuccess.ShouldBeTrue();
        return result.Value.Id;
    }

    private async Task AdvanceAsync(Guid orderId, OrderMilestone milestone)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<AdvanceOrderHandler>()
            .HandleAsync(new AdvanceOrderCommand(Guid.CreateVersion7(), "ordering.test", orderId, milestone), Cancel);

        result.IsSuccess.ShouldBeTrue();
    }

    private Task<HttpResponseMessage> ListAsync(Customer customer, string query = "") =>
        AuthClient.GetAsync(fixture.Client, "/api/v1/orders" + query, customer.Token);

    private Task<HttpResponseMessage> GetAsync(Customer customer, Guid id) =>
        AuthClient.GetAsync(fixture.Client, $"/api/v1/orders/{id}", customer.Token);

    private async Task<HttpResponseMessage> CancelAsync(
        Customer customer,
        Guid id,
        string? ifMatch,
        string? key,
        object? body = null
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/orders/{id}/cancellation", UriKind.Relative)
        )
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body ?? new { reasonCode = "changedMind" }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await fixture.Client.SendAsync(request, Cancel);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private static string NewKey() => Guid.NewGuid().ToString();

    // --- Reading (BR-ORD-002, BR-ORD-006) ---

    [Fact]
    public async Task Should_read_an_order_with_its_price_snapshot_etag_and_allowed_actions()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer, 189.90m, 2);

        using var response = await GetAsync(customer, id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        var body = await ReadAsync(response);
        body.GetProperty("id").GetGuid().ShouldBe(id);
        body.GetProperty("number").GetString()!.ShouldMatch("^PF-[0-9]{4}-[0-9]{6,}$");
        body.GetProperty("status").GetString().ShouldBe("awaitingPayment");
        body.GetProperty("total").GetProperty("amount").GetString().ShouldBe("379.80");
        body.GetProperty("total").GetProperty("currency").GetString().ShouldBe("BRL");
        body.GetProperty("placedAt").GetString().ShouldEndWith("Z");
        body.GetProperty("version").GetInt32().ShouldBe(1);
        body.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldBe(["cancel"]);
        var line = body.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("sku").GetString().ShouldBe("CAF-600-PRT");
        line.GetProperty("name").GetString().ShouldBe("Cafeteira Elétrica 600ml");
        line.GetProperty("quantity").GetInt32().ShouldBe(2);
        line.GetProperty("unitPrice").GetProperty("amount").GetString().ShouldBe("189.90");
        line.GetProperty("lineTotal").GetProperty("amount").GetString().ShouldBe("379.80");
    }

    [Fact]
    public async Task Should_answer_404_for_an_order_of_another_customer_exactly_like_a_missing_one()
    {
        var owner = await NewCustomerAsync();
        var id = await PlaceAsync(owner);
        var intruder = await NewCustomerAsync();

        using var foreign = await GetAsync(intruder, id);
        using var missing = await GetAsync(intruder, Guid.NewGuid());

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignBody = await ReadAsync(foreign);
        var missingBody = await ReadAsync(missing);
        foreignBody.GetProperty("code").GetString().ShouldBe("ORDER_NOT_FOUND");
        foreignBody.GetProperty("code").GetString().ShouldBe(missingBody.GetProperty("code").GetString());
        foreignBody.GetProperty("title").GetString().ShouldBe(missingBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_not_let_staff_read_the_orders_of_a_customer_through_the_customer_endpoints()
    {
        var owner = await NewCustomerAsync();
        var id = await PlaceAsync(owner);
        var administrator = new Customer(Guid.Empty, fixture.TokenFor("administrator").AccessToken);

        using var response = await GetAsync(administrator, id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Listing (BR-ORD-006) ---

    [Fact]
    public async Task Should_answer_an_empty_page_with_200_for_a_customer_without_orders()
    {
        var customer = await NewCustomerAsync();

        using var response = await ListAsync(customer);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await ReadAsync(response);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Should_list_only_the_callers_orders_newest_first()
    {
        var customer = await NewCustomerAsync();
        var other = await NewCustomerAsync();
        var first = await PlaceAsync(customer);
        await PlaceAsync(other);
        var second = await PlaceAsync(customer);

        using var response = await ListAsync(customer);

        var items = (await ReadAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        items[0].GetProperty("status").GetString().ShouldBe("awaitingPayment");
        items[0].GetProperty("total").GetProperty("amount").GetString().ShouldBe("379.80");
        items[0].GetProperty("placedAt").GetString().ShouldEndWith("Z");
    }

    [Fact]
    public async Task Should_walk_every_page_without_duplicates_or_gaps()
    {
        var customer = await NewCustomerAsync();
        var placed = new List<Guid>();
        foreach (var index in Enumerable.Range(1, 5))
        {
            placed.Add(await PlaceAsync(customer, price: 10m * index));
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var response = await ListAsync(
                customer,
                "?limit=2&sort=total" + (cursor is null ? "" : $"&cursor={cursor}")
            );
            var page = await ReadAsync(response);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            page.GetProperty("hasMore").GetBoolean().ShouldBe(cursor is not null);
            pages++;
        } while (cursor is not null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe(placed);
    }

    [Fact]
    public async Task Should_sort_by_total_in_either_direction_and_filter_by_status()
    {
        var customer = await NewCustomerAsync();
        var cheap = await PlaceAsync(customer, 10m, 1);
        var dear = await PlaceAsync(customer, 300m, 1);
        await AdvanceAsync(dear, OrderMilestone.Paid);

        using var ascending = await ListAsync(customer, "?sort=total");
        using var descending = await ListAsync(customer, "?sort=-total");
        using var paid = await ListAsync(customer, "?status=paid");
        using var awaiting = await ListAsync(customer, "?status=awaitingPayment");

        IdsOf(await ReadAsync(ascending)).ShouldBe([cheap, dear]);
        IdsOf(await ReadAsync(descending)).ShouldBe([dear, cheap]);
        IdsOf(await ReadAsync(paid)).ShouldBe([dear]);
        IdsOf(await ReadAsync(awaiting)).ShouldBe([cheap]);

        static Guid[] IdsOf(JsonElement page) =>
            [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    [Fact]
    public async Task Should_filter_by_the_start_date_in_utc()
    {
        var customer = await NewCustomerAsync();
        await PlaceAsync(customer);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var tomorrow = today.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var future = await ListAsync(customer, $"?createdFrom={tomorrow}");
        using var past = await ListAsync(customer, $"?createdFrom={yesterday}");

        (await ReadAsync(future)).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await ReadAsync(past)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Theory]
    [InlineData("?sort=number", "SORT_FIELD_NOT_ALLOWED")]
    [InlineData("?sort=-status", "SORT_FIELD_NOT_ALLOWED")]
    [InlineData("?cursor=bm90LWEtY3Vyc29y.AAAA", "PAGE_CURSOR_INVALID")]
    public async Task Should_answer_400_with_a_stable_code_for_an_unknown_sort_field_or_a_forged_cursor(
        string query,
        string code
    )
    {
        var customer = await NewCustomerAsync();

        using var response = await ListAsync(customer, query);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe(code);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?status=archived")]
    [InlineData("?createdFrom=yesterday")]
    public async Task Should_answer_400_for_malformed_collection_parameters(string query)
    {
        using var response = await ListAsync(await NewCustomerAsync(), query);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_never_honour_a_cursor_issued_to_another_customer()
    {
        var owner = await NewCustomerAsync();
        await PlaceAsync(owner);
        await PlaceAsync(owner);
        using var first = await ListAsync(owner, "?limit=1");
        var cursor = (await ReadAsync(first)).GetProperty("nextCursor").GetString();
        var thief = await NewCustomerAsync();

        using var stolen = await ListAsync(thief, $"?limit=1&cursor={cursor}");

        stolen.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthClient.ReadCodeAsync(stolen)).ShouldBe("PAGE_CURSOR_INVALID");
    }

    // --- Cancelling (BR-ORD-003, BR-ORD-004) ---

    [Fact]
    public async Task Should_cancel_an_order_and_advance_the_etag_and_drop_the_allowed_actions()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);

        using var response = await CancelAsync(
            customer,
            id,
            "\"1\"",
            NewKey(),
            new { reasonCode = "changedMind", note = "Comprei sem querer" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(response);
        body.GetProperty("status").GetString().ShouldBe("cancelled");
        body.GetProperty("version").GetInt32().ShouldBe(2);
        body.GetProperty("allowedActions").GetArrayLength().ShouldBe(0);
        (await ReadAsync(await GetAsync(customer, id))).GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Fact]
    public async Task Should_publish_the_cancellation_event_with_whether_the_order_was_paid_and_without_the_note()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);
        await AdvanceAsync(id, OrderMilestone.Paid);

        using var response = await CancelAsync(
            customer,
            id,
            "\"2\"",
            NewKey(),
            new { reasonCode = "foundCheaper", note = "dados pessoais aqui" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await fixture.Factory.Database.ScalarAsync(
                $"SELECT payload ->> 'wasPaid' FROM ordering.outbox_messages WHERE aggregate_id = '{id}' AND type LIKE '%OrderCancelled'"
            )
        ).ShouldBe("true");
        (
            await fixture.Factory.Database.ScalarAsync(
                $"SELECT count(*) FROM ordering.outbox_messages WHERE aggregate_id = '{id}' AND payload::text LIKE '%dados pessoais%'"
            )
        ).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_answer_a_retried_cancellation_with_the_cancelled_order_and_cancel_nothing_twice()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);
        var key = NewKey();
        using var first = await CancelAsync(customer, id, "\"1\"", key);

        // The retry carries the version the original read, which the original already advanced.
        using var retry = await CancelAsync(customer, id, "\"1\"", key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(retry)).GetProperty("version").GetInt32().ShouldBe(2);
        (
            await fixture.Factory.Database.ScalarAsync(
                $"SELECT count(*) FROM ordering.outbox_messages WHERE aggregate_id = '{id}' AND type LIKE '%OrderCancelled'"
            )
        ).ShouldBe(1L);
    }

    [Fact]
    public async Task Should_refuse_reusing_an_idempotency_key_for_a_different_cancellation_with_a_422()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);
        var key = NewKey();
        using var first = await CancelAsync(customer, id, "\"1\"", key, new { reasonCode = "changedMind" });

        using var reuse = await CancelAsync(customer, id, "\"2\"", key, new { reasonCode = "foundCheaper" });

        reuse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(reuse)).ShouldBe("IDEMPOTENCY_KEY_REUSED");
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("W/\"7\"")]
    [InlineData("\"abc\"")]
    [InlineData("*")]
    [InlineData("1")]
    public async Task Should_answer_412_when_if_match_is_stale_or_malformed_and_change_nothing(string ifMatch)
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);

        using var response = await CancelAsync(customer, id, ifMatch, NewKey());
        using var read = await GetAsync(customer, id);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("ORDER_VERSION_MISMATCH");
        (await ReadAsync(read)).GetProperty("status").GetString().ShouldBe("awaitingPayment");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Should_answer_400_when_the_precondition_or_the_key_is_missing_or_the_key_too_short(
        bool withIfMatch,
        bool withKey
    )
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);

        using var response = await CancelAsync(customer, id, withIfMatch ? "\"1\"" : null, withKey ? "short" : null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("a", "ORDER_REASON_INVALID")]
    [InlineData("1abc", "ORDER_REASON_INVALID")]
    [InlineData("changed mind", "ORDER_REASON_INVALID")]
    public async Task Should_answer_422_for_a_reason_that_is_not_a_short_identifier(string reason, string code)
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);

        using var response = await CancelAsync(customer, id, "\"1\"", NewKey(), new { reasonCode = reason });

        // One character is also below the contract's minimum length, which is answered before the use case runs.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            (await AuthClient.ReadCodeAsync(response)).ShouldBe(code);
        }
    }

    [Fact]
    public async Task Should_answer_400_for_a_note_above_five_hundred_characters_and_for_a_missing_reason()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);

        using var longNote = await CancelAsync(
            customer,
            id,
            "\"1\"",
            NewKey(),
            new { reasonCode = "changedMind", note = new string('x', 501) }
        );
        using var noReason = await CancelAsync(customer, id, "\"1\"", NewKey(), new { note = "só uma nota" });

        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    public async Task Should_answer_409_for_an_order_that_can_no_longer_be_cancelled(string status)
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);
        await AdvanceAsync(id, OrderMilestone.Paid);
        await AdvanceAsync(id, OrderMilestone.Shipped);
        if (status == "Delivered")
        {
            await AdvanceAsync(id, OrderMilestone.Delivered);
        }

        using var read = await GetAsync(customer, id);
        var version = read.Headers.ETag!.Tag;
        using var response = await CancelAsync(customer, id, version, NewKey());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("ORDER_INVALID_STATUS_TRANSITION");
        (await ReadAsync(read)).GetProperty("allowedActions").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Should_answer_409_for_a_second_cancellation_under_another_key()
    {
        var customer = await NewCustomerAsync();
        var id = await PlaceAsync(customer);
        using var first = await CancelAsync(customer, id, "\"1\"", NewKey());

        using var second = await CancelAsync(customer, id, "\"2\"", NewKey());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Should_answer_404_when_cancelling_an_order_of_another_customer_and_leave_it_untouched()
    {
        var owner = await NewCustomerAsync();
        var id = await PlaceAsync(owner);
        var intruder = await NewCustomerAsync();

        using var response = await CancelAsync(intruder, id, "\"1\"", NewKey());
        using var read = await GetAsync(owner, id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(read)).GetProperty("status").GetString().ShouldBe("awaitingPayment");
    }

    [Fact]
    public async Task Should_not_recognise_a_cancellation_retry_made_by_another_customer_even_with_the_same_key()
    {
        var owner = await NewCustomerAsync();
        var id = await PlaceAsync(owner);
        var key = NewKey();
        using var first = await CancelAsync(owner, id, "\"1\"", key);
        var intruder = await NewCustomerAsync();

        using var retry = await CancelAsync(intruder, id, "\"1\"", key);

        retry.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
