using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Shipping.Application;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The shipment listing through the real pipeline and a real PostgreSQL (BR-SHP-004): only the owner of an order sees its
/// shipments, an order that is someone else's answers exactly like one that does not exist, and an order without a shipment
/// yet answers an empty page. The worker role does not run here, so the Ordering events are handed to the real handler the
/// consumer calls; <see cref="Messaging.OrderFulfillmentFlowTests"/> covers the broker in between.
/// </summary>
public sealed class ShipmentsTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private sealed record Customer(Guid Id, string Token);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<Customer> NewCustomerAsync()
    {
        var email = $"receiver.{Guid.NewGuid():N}@example.com";
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

    private async Task SyncAsync(OrderEventKind kind, Guid order, Customer customer)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<SyncOrderHandler>()
            .HandleAsync(
                new SyncOrderCommand(Guid.CreateVersion7(), kind, order, customer.Id, "PF-2026-000001"),
                Cancel
            );
        result.IsSuccess.ShouldBeTrue();
    }

    private async Task<Guid> ShipmentOfAsync(Guid order)
    {
        var id = await fixture.Factory.Database.ScalarAsync(
            $"SELECT id FROM shipping.shipments WHERE order_id = '{order}'"
        );
        return (Guid)id!;
    }

    private async Task DispatchAsync(
        Guid shipment,
        string carrier = "Correios",
        string? tracking = "BR123456789",
        DateOnly? estimate = null
    )
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<DispatchShipmentHandler>()
            .HandleAsync(new DispatchShipmentCommand(shipment, carrier, tracking, estimate), Cancel);
        result.IsSuccess.ShouldBeTrue();
    }

    private Task<HttpResponseMessage> ListAsync(Customer customer, Guid order, string query = "") =>
        AuthClient.GetAsync(fixture.Client, $"/api/v1/orders/{order}/shipments{query}", customer.Token);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    [Fact]
    public async Task Should_answer_an_empty_page_with_200_for_an_order_of_the_caller_that_has_no_shipment_yet()
    {
        var customer = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Placed, order, customer);

        using var response = await ListAsync(customer, order);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await ReadAsync(response);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Should_show_the_shipment_being_prepared_without_carrier_data_once_the_order_is_paid()
    {
        var customer = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Placed, order, customer);
        await SyncAsync(OrderEventKind.Paid, order, customer);

        using var response = await ListAsync(customer, order);

        var shipment = (await ReadAsync(response)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        shipment.GetProperty("orderId").GetGuid().ShouldBe(order);
        shipment.GetProperty("status").GetString().ShouldBe("preparing");
        // Optional members are omitted until they exist, never returned as null (ai/API_CONTRACTS.md §3).
        shipment.TryGetProperty("carrier", out _).ShouldBeFalse();
        shipment.TryGetProperty("trackingCode", out _).ShouldBeFalse();
        shipment.TryGetProperty("estimatedDeliveryDate", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_show_the_carrier_the_tracking_code_and_the_estimate_as_a_calendar_date_once_dispatched()
    {
        var customer = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Paid, order, customer);
        var estimate = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(4);
        await DispatchAsync(await ShipmentOfAsync(order), "Correios", "BR123456789", estimate);

        using var response = await ListAsync(customer, order);

        var shipment = (await ReadAsync(response)).GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        shipment.GetProperty("status").GetString().ShouldBe("inTransit");
        shipment.GetProperty("carrier").GetString().ShouldBe("Correios");
        shipment.GetProperty("trackingCode").GetString().ShouldBe("BR123456789");
        shipment
            .GetProperty("estimatedDeliveryDate")
            .GetString()
            .ShouldBe(estimate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Should_show_a_shipment_that_was_cancelled_with_its_order()
    {
        var customer = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Paid, order, customer);
        await SyncAsync(OrderEventKind.Cancelled, order, customer);

        using var response = await ListAsync(customer, order);

        (await ReadAsync(response)).GetProperty("items")[0].GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Fact]
    public async Task Should_answer_404_for_an_order_of_another_customer_exactly_like_one_that_does_not_exist()
    {
        var owner = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Paid, order, owner);
        var intruder = await NewCustomerAsync();

        using var foreign = await ListAsync(intruder, order);
        using var missing = await ListAsync(intruder, Guid.NewGuid());

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var foreignBody = await ReadAsync(foreign);
        var missingBody = await ReadAsync(missing);
        foreignBody.GetProperty("code").GetString().ShouldBe("ORDER_NOT_FOUND");
        foreignBody.GetProperty("code").GetString().ShouldBe(missingBody.GetProperty("code").GetString());
        foreignBody.GetProperty("title").GetString().ShouldBe(missingBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_not_let_staff_list_the_shipments_of_a_customers_order()
    {
        var owner = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Paid, order, owner);

        using var response = await AuthClient.GetAsync(
            fixture.Client,
            $"/api/v1/orders/{order}/shipments",
            fixture.TokenFor("administrator").AccessToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?cursor=bm90LWEtY3Vyc29y.AAAA")]
    public async Task Should_answer_400_for_malformed_collection_parameters_and_a_forged_cursor(string query)
    {
        var customer = await NewCustomerAsync();
        var order = Guid.CreateVersion7();
        await SyncAsync(OrderEventKind.Placed, order, customer);

        using var response = await ListAsync(customer, order, query);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_answer_404_for_a_malformed_order_identifier_in_the_route()
    {
        var customer = await NewCustomerAsync();

        using var response = await AuthClient.GetAsync(
            fixture.Client,
            "/api/v1/orders/not-a-guid/shipments",
            customer.Token
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
