using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Cart.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The Cart use cases through the real pipeline and a real PostgreSQL (BR-CRT-001 to BR-CRT-005): one cart per customer,
/// lines priced by the server from the Cart's view of the catalog, ownership, and the errors of each rule. The worker role
/// does not run here, so each test delivers the Catalog events to the real handler the consumer calls;
/// <see cref="Messaging.CartCatalogFlowTests"/> covers the broker in between.
/// </summary>
public sealed class CartTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Each customer has one active cart, so a test that needs a cart of its own needs a customer of its own.
    private async Task<string> NewCustomerAsync()
    {
        var email = $"shopper.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email, password }
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (
            await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password))
        ).AccessToken;
    }

    // What the worker does for each Catalog event: hand it to the handler.
    private async Task<Guid> SeedProductAsync(
        decimal price = 100m,
        string currency = "BRL",
        bool sellable = true,
        string? name = null
    )
    {
        var id = Guid.CreateVersion7();
        var sku = "T-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

        await SyncAsync(
            new SyncCatalogProductCommand(
                Guid.CreateVersion7(),
                CatalogProductEventKind.Created,
                id,
                1,
                sku,
                name ?? "Cafeteira Elétrica",
                new Money(price, currency),
                null
            )
        );
        if (sellable)
        {
            await SyncAsync(Status(id, 2, "active"));
        }

        return id;
    }

    private static SyncCatalogProductCommand Status(Guid id, int version, string status) =>
        new(Guid.CreateVersion7(), CatalogProductEventKind.StatusChanged, id, version, null, null, null, status);

    private async Task SyncAsync(SyncCatalogProductCommand command)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<SyncCatalogProductHandler>()
            .HandleAsync(command, Cancel);
        result.IsSuccess.ShouldBeTrue();
    }

    private Task<HttpResponseMessage> OpenAsync(string token, string currency = "BRL") =>
        AuthClient.SendJsonAsync(fixture.Client, HttpMethod.Post, "/api/v1/carts", token, new { currency });

    private Task<HttpResponseMessage> AddAsync(string token, Guid cartId, Guid productId, int quantity = 1) =>
        AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/carts/{cartId}/items",
            token,
            new { productId, quantity }
        );

    private Task<HttpResponseMessage> GetAsync(string token, Guid cartId) =>
        AuthClient.GetAsync(fixture.Client, $"/api/v1/carts/{cartId}", token);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    private async Task<Guid> OpenedCartAsync(string token)
    {
        using var response = await OpenAsync(token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static string[] ActionsOf(JsonElement cart) =>
        [.. cart.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetString()!)];

    // --- Opening (BR-CRT-001) ---

    [Fact]
    public async Task Should_open_an_empty_cart_with_its_location_etag_and_allowed_actions()
    {
        var token = await NewCustomerAsync();

        using var response = await OpenAsync(token, "BRL");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location?.OriginalString.ShouldBe($"/api/v1/carts/{id}");
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("items").GetArrayLength().ShouldBe(0);
        body.GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("0.00");
        body.GetProperty("subtotal").GetProperty("currency").GetString().ShouldBe("BRL");
        body.GetProperty("version").GetInt32().ShouldBe(1);
        ActionsOf(body).ShouldBe(["add-item"]);
    }

    [Fact]
    public async Task Should_answer_a_second_request_with_the_cart_that_is_already_open_instead_of_creating_another()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);

        using var again = await OpenAsync(token, "BRL");

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(again)).GetProperty("id").GetGuid().ShouldBe(id);
        again.Headers.Location?.OriginalString.ShouldBe($"/api/v1/carts/{id}");
    }

    [Fact]
    public async Task Should_answer_409_for_a_cart_in_another_currency_while_one_is_open()
    {
        var token = await NewCustomerAsync();
        await OpenedCartAsync(token);

        using var other = await OpenAsync(token, "USD");

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(other)).ShouldBe("CART_ALREADY_OPEN");
    }

    [Fact]
    public async Task Should_give_every_customer_a_cart_of_their_own()
    {
        var first = await OpenedCartAsync(await NewCustomerAsync());
        var second = await OpenedCartAsync(await NewCustomerAsync());

        second.ShouldNotBe(first);
    }

    [Theory]
    [InlineData("BR")]
    [InlineData("B2N")]
    [InlineData("")]
    public async Task Should_answer_400_for_a_currency_that_is_not_an_iso_code(string currency)
    {
        using var response = await OpenAsync(await NewCustomerAsync(), currency);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Reading and ownership ---

    [Fact]
    public async Task Should_read_the_cart_with_its_etag()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);

        using var response = await GetAsync(token, id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        (await ReadAsync(response)).GetProperty("id").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task Should_answer_404_for_a_cart_of_another_customer_exactly_like_a_missing_one()
    {
        var owner = await NewCustomerAsync();
        var id = await OpenedCartAsync(owner);
        var intruder = await NewCustomerAsync();

        using var foreign = await GetAsync(intruder, id);
        using var missing = await GetAsync(intruder, Guid.NewGuid());

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignBody = await ReadAsync(foreign);
        var missingBody = await ReadAsync(missing);
        foreignBody.GetProperty("code").GetString().ShouldBe("CART_NOT_FOUND");
        foreignBody.GetProperty("title").GetString().ShouldBe(missingBody.GetProperty("title").GetString());
        foreignBody.GetProperty("code").GetString().ShouldBe(missingBody.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Should_never_let_another_customer_change_a_cart()
    {
        var owner = await NewCustomerAsync();
        var id = await OpenedCartAsync(owner);
        var product = await SeedProductAsync();
        var intruder = await NewCustomerAsync();
        using var added = await AddAsync(owner, id, product);
        var lineId = (await ReadAsync(added)).GetProperty("items")[0].GetProperty("id").GetGuid();

        using var add = await AddAsync(intruder, id, product);
        using var remove = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            intruder
        );
        using var read = await GetAsync(owner, id);

        add.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        remove.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(read)).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    // --- Adding (BR-CRT-002, BR-CRT-003, BR-CRT-005) ---

    [Fact]
    public async Task Should_add_a_product_priced_by_the_server_and_advance_the_etag()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(189.90m, name: "Cafeteira Elétrica 600ml");

        using var response = await AddAsync(token, id, product, 2);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(response);
        var line = body.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("productId").GetGuid().ShouldBe(product);
        line.GetProperty("name").GetString().ShouldBe("Cafeteira Elétrica 600ml");
        line.GetProperty("quantity").GetInt32().ShouldBe(2);
        line.GetProperty("unitPrice").GetProperty("amount").GetString().ShouldBe("189.90");
        line.GetProperty("lineTotal").GetProperty("amount").GetString().ShouldBe("379.80");
        body.GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("379.80");
        body.GetProperty("version").GetInt32().ShouldBe(2);
        ActionsOf(body).ShouldBe(["add-item", "remove-item", "checkout"]);
    }

    [Fact]
    public async Task Should_ignore_a_price_the_client_sends()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(100m);

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/carts/{id}/items",
            token,
            new
            {
                productId = product,
                quantity = 1,
                unitPrice = new { amount = "0.01", currency = "BRL" },
                subtotal = "0.01",
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("100.00");
    }

    [Fact]
    public async Task Should_increase_the_line_when_the_same_product_is_added_again()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(10m);
        using var first = await AddAsync(token, id, product, 2);

        using var second = await AddAsync(token, id, product, 3);

        var line = (await ReadAsync(second)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("quantity").GetInt32().ShouldBe(5);
        line.GetProperty("lineTotal").GetProperty("amount").GetString().ShouldBe("50.00");
        (await ReadAsync(first))
            .GetProperty("items")[0]
            .GetProperty("id")
            .GetGuid()
            .ShouldBe(line.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Should_sum_several_lines_into_the_subtotal()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var coffee = await SeedProductAsync(189.90m);
        var grinder = await SeedProductAsync(49.50m);
        await AddAsync(token, id, coffee, 2);

        using var response = await AddAsync(token, id, grinder, 3);

        var body = await ReadAsync(response);
        body.GetProperty("items").GetArrayLength().ShouldBe(2);
        body.GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("528.30");
    }

    [Fact]
    public async Task Should_follow_a_price_change_of_the_catalog_when_the_cart_is_read_again()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(100m);
        await AddAsync(token, id, product, 2);

        await SyncAsync(
            new SyncCatalogProductCommand(
                Guid.CreateVersion7(),
                CatalogProductEventKind.PriceChanged,
                product,
                3,
                null,
                null,
                new Money(120m, "BRL"),
                null
            )
        );
        using var read = await GetAsync(token, id);

        var body = await ReadAsync(read);
        body.GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("240.00");
        read.Headers.ETag?.Tag.ShouldBe("\"2\"");
    }

    [Theory]
    [InlineData(false, "BRL", "CART_PRODUCT_NOT_AVAILABLE")]
    [InlineData(true, "USD", "CART_CURRENCY_MISMATCH")]
    public async Task Should_answer_422_for_a_product_that_is_not_sellable_or_priced_in_another_currency(
        bool sellable,
        string currency,
        string code
    )
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(sellable: sellable, currency: currency);

        using var response = await AddAsync(token, id, product);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe(code);
    }

    [Fact]
    public async Task Should_answer_422_for_a_product_the_cart_does_not_know()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);

        using var response = await AddAsync(token, id, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("CART_PRODUCT_NOT_AVAILABLE");
    }

    [Fact]
    public async Task Should_stop_selling_a_product_the_catalog_deleted()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync();
        await SyncAsync(
            new SyncCatalogProductCommand(
                Guid.CreateVersion7(),
                CatalogProductEventKind.Deleted,
                product,
                3,
                null,
                null,
                null,
                null
            )
        );

        using var response = await AddAsync(token, id, product);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-3)]
    public async Task Should_answer_400_for_a_quantity_outside_one_to_ninety_nine(int quantity)
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync();

        using var response = await AddAsync(token, id, product, quantity);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("""{"quantity":1}""")]
    [InlineData("""{"productId":"0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f01"}""")]
    [InlineData("""{"productId":"not-a-guid","quantity":1}""")]
    [InlineData("""{}""")]
    public async Task Should_answer_400_for_a_body_missing_the_product_or_the_quantity(string body)
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/carts/{id}/items", UriKind.Relative)
        )
        {
            Content = content,
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(request, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_answer_422_when_a_line_would_pass_ninety_nine_units_and_keep_the_cart_unchanged()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync();
        await AddAsync(token, id, product, 90);

        using var overflow = await AddAsync(token, id, product, 10);
        using var read = await GetAsync(token, id);

        overflow.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(overflow)).ShouldBe("CART_LINE_QUANTITY_LIMIT");
        (await ReadAsync(read)).GetProperty("items")[0].GetProperty("quantity").GetInt32().ShouldBe(90);
    }

    [Fact]
    public async Task Should_answer_404_when_adding_to_a_cart_that_does_not_exist()
    {
        var token = await NewCustomerAsync();
        var product = await SeedProductAsync();

        using var response = await AddAsync(token, Guid.NewGuid(), product);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("CART_NOT_FOUND");
    }

    // --- Removing ---

    [Fact]
    public async Task Should_remove_a_line_and_return_the_remaining_cart()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var kept = await SeedProductAsync(100m);
        var removed = await SeedProductAsync(50m);
        await AddAsync(token, id, kept);
        using var added = await AddAsync(token, id, removed, 2);
        var lineId = (await ReadAsync(added))
            .GetProperty("items")
            .EnumerateArray()
            .Single(line => line.GetProperty("productId").GetGuid() == removed)
            .GetProperty("id")
            .GetGuid();

        using var response = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            token
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("items")
            .EnumerateArray()
            .ShouldHaveSingleItem()
            .GetProperty("productId")
            .GetGuid()
            .ShouldBe(kept);
        body.GetProperty("subtotal").GetProperty("amount").GetString().ShouldBe("100.00");
        body.GetProperty("version").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task Should_answer_404_for_a_line_that_is_not_in_the_cart_and_for_one_already_removed()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync();
        using var added = await AddAsync(token, id, product);
        var lineId = (await ReadAsync(added)).GetProperty("items")[0].GetProperty("id").GetGuid();
        using var removed = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            token
        );

        using var again = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            token
        );
        using var unknown = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{Guid.NewGuid()}",
            token
        );

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(again)).ShouldBe("CART_ITEM_NOT_FOUND");
    }

    [Fact]
    public async Task Should_let_a_removed_product_be_added_again()
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync(10m);
        using var added = await AddAsync(token, id, product, 5);
        var lineId = (await ReadAsync(added)).GetProperty("items")[0].GetProperty("id").GetGuid();
        using var removed = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            token
        );

        using var again = await AddAsync(token, id, product, 1);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var line = (await ReadAsync(again)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("quantity").GetInt32().ShouldBe(1);
        line.GetProperty("id").GetGuid().ShouldNotBe(lineId);
    }

    // --- Lifecycle (BR-CRT-004) ---

    [Theory]
    [InlineData("CheckedOut")]
    [InlineData("Expired")]
    public async Task Should_answer_409_for_any_change_to_a_cart_that_is_no_longer_active(string status)
    {
        var token = await NewCustomerAsync();
        var id = await OpenedCartAsync(token);
        var product = await SeedProductAsync();
        using var added = await AddAsync(token, id, product);
        var lineId = (await ReadAsync(added)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await fixture.Factory.Database.ExecuteAsync($"UPDATE cart.carts SET status = '{status}' WHERE id = '{id}'");

        using var add = await AddAsync(token, id, product);
        using var remove = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Delete,
            $"/api/v1/carts/{id}/items/{lineId}",
            token
        );
        using var read = await GetAsync(token, id);

        add.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        remove.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(add)).ShouldBe("CART_NOT_ACTIVE");
        var body = await ReadAsync(read);
        body.GetProperty("status").GetString().ShouldBe(status == "CheckedOut" ? "checkedOut" : "expired");
        ActionsOf(body).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_open_a_new_cart_once_the_previous_one_was_checked_out()
    {
        var token = await NewCustomerAsync();
        var first = await OpenedCartAsync(token);
        await fixture.Factory.Database.ExecuteAsync(
            $"UPDATE cart.carts SET status = 'CheckedOut' WHERE id = '{first}'"
        );

        using var response = await OpenAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadAsync(response)).GetProperty("id").GetGuid().ShouldNotBe(first);
    }
}
