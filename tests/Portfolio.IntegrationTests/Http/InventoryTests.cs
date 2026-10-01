using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The Inventory use cases through the real pipeline: opening an item, reading it, and adjusting stock with
/// <c>If-Match</c> and <c>Idempotency-Key</c> (BR-INV-001, BR-INV-003, BR-INV-008).
/// </summary>
public sealed class InventoryTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private string Manager => fixture.TokenFor("manager").AccessToken;

    private string Collaborator => fixture.TokenFor("collaborator").AccessToken;

    private static string NewSku() => "T-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    private Task<HttpResponseMessage> OpenAsync(string sku) =>
        AuthClient.SendJsonAsync(fixture.Client, HttpMethod.Post, "/api/v1/inventory/items", Manager, new { sku });

    private async Task<HttpResponseMessage> AdjustAsync(
        string sku,
        string? ifMatch,
        string? key,
        object body,
        string? token = null
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/inventory/items/{sku}/adjustments", UriKind.Relative)
        )
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Manager);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private async Task<string> OpenedSkuAsync()
    {
        var sku = NewSku();
        using var response = await OpenAsync(sku);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return sku;
    }

    [Fact]
    public async Task Should_open_an_empty_item_that_a_collaborator_can_read()
    {
        var sku = NewSku();

        using var created = await OpenAsync(sku.ToLowerInvariant());
        using var read = await AuthClient.GetAsync(fixture.Client, $"/api/v1/inventory/items/{sku}", Collaborator);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location?.OriginalString.ShouldBe($"/api/v1/inventory/items/{sku}");
        created.Headers.ETag?.Tag.ShouldBe("\"1\"");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.ETag?.Tag.ShouldBe("\"1\"");
        var body = await ReadAsync(read);
        body.GetProperty("sku").GetString().ShouldBe(sku);
        body.GetProperty("onHand").GetInt32().ShouldBe(0);
        body.GetProperty("reserved").GetInt32().ShouldBe(0);
        body.GetProperty("available").GetInt32().ShouldBe(0);
        body.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Should_refuse_opening_the_same_sku_twice()
    {
        var sku = await OpenedSkuAsync();

        using var second = await OpenAsync(sku);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(second)).ShouldBe("INVENTORY_ITEM_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Should_answer_404_for_a_sku_without_an_item()
    {
        using var response = await AuthClient.GetAsync(
            fixture.Client,
            $"/api/v1/inventory/items/{NewSku()}",
            Collaborator
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("INVENTORY_ITEM_NOT_FOUND");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("has%20space")]
    public async Task Should_answer_400_for_a_malformed_sku_in_the_route(string sku)
    {
        using var response = await AuthClient.GetAsync(fixture.Client, $"/api/v1/inventory/items/{sku}", Collaborator);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_adjust_stock_and_advance_the_etag()
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(
            sku,
            "\"1\"",
            Guid.NewGuid().ToString(),
            new { delta = 12, reasonCode = "stocktake" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(response);
        body.GetProperty("onHand").GetInt32().ShouldBe(12);
        body.GetProperty("available").GetInt32().ShouldBe(12);
        body.GetProperty("version").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Should_replay_a_retried_adjustment_without_adjusting_twice()
    {
        var sku = await OpenedSkuAsync();
        var key = Guid.NewGuid().ToString();
        var body = new { delta = 5, reasonCode = "stocktake" };
        using var first = await AdjustAsync(sku, "\"1\"", key, body);

        using var retry = await AdjustAsync(sku, "\"1\"", key, body);
        using var read = await AuthClient.GetAsync(fixture.Client, $"/api/v1/inventory/items/{sku}", Collaborator);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(retry)).GetProperty("onHand").GetInt32().ShouldBe(5);
        (await ReadAsync(read)).GetProperty("onHand").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task Should_refuse_reusing_an_idempotency_key_for_a_different_adjustment()
    {
        var sku = await OpenedSkuAsync();
        var key = Guid.NewGuid().ToString();
        using var first = await AdjustAsync(sku, "\"1\"", key, new { delta = 5, reasonCode = "stocktake" });

        using var reuse = await AdjustAsync(sku, "\"2\"", key, new { delta = 9, reasonCode = "stocktake" });

        reuse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(reuse)).ShouldBe("IDEMPOTENCY_KEY_REUSED");
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("W/\"7\"")]
    [InlineData("\"abc\"")]
    [InlineData("*")]
    [InlineData("1")]
    public async Task Should_answer_412_when_if_match_is_stale_or_malformed(string ifMatch)
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(
            sku,
            ifMatch,
            Guid.NewGuid().ToString(),
            new { delta = 5, reasonCode = "stocktake" }
        );
        using var read = await AuthClient.GetAsync(fixture.Client, $"/api/v1/inventory/items/{sku}", Collaborator);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("INVENTORY_VERSION_MISMATCH");
        (await ReadAsync(read)).GetProperty("onHand").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Should_accept_a_weak_if_match_for_the_current_version()
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(
            sku,
            "W/\"1\"",
            Guid.NewGuid().ToString(),
            new { delta = 1, reasonCode = "stocktake" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_answer_422_when_the_adjustment_would_take_the_stock_below_zero()
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(
            sku,
            "\"1\"",
            Guid.NewGuid().ToString(),
            new { delta = -1, reasonCode = "damage" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        problem.RootElement.GetProperty("code").GetString().ShouldBe("STOCK_BELOW_RESERVED");
        var error = problem.RootElement.GetProperty("errors")[0];
        error.GetProperty("ruleId").GetString().ShouldBe("BR-INV-001");
        error.GetProperty("field").GetString().ShouldBe("/delta");
    }

    [Theory]
    [InlineData(0, "stocktake", "STOCK_ADJUSTMENT_DELTA_ZERO")]
    [InlineData(5, "Not Valid!", "STOCK_REASON_INVALID")]
    public async Task Should_answer_422_when_the_adjustment_breaks_a_rule(int delta, string reason, string code)
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(
            sku,
            "\"1\"",
            Guid.NewGuid().ToString(),
            new { delta, reasonCode = reason }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe(code);
    }

    [Theory]
    [InlineData(null, "key-0001-key-0001")]
    [InlineData("\"1\"", null)]
    public async Task Should_answer_400_when_a_required_header_is_missing(string? ifMatch, string? key)
    {
        var sku = await OpenedSkuAsync();

        using var response = await AdjustAsync(sku, ifMatch, key, new { delta = 5, reasonCode = "stocktake" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_answer_404_when_adjusting_a_sku_without_an_item()
    {
        using var response = await AdjustAsync(
            NewSku(),
            "\"1\"",
            Guid.NewGuid().ToString(),
            new { delta = 5, reasonCode = "stocktake" }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_keep_adjusting_and_opening_out_of_a_collaborators_reach()
    {
        var sku = await OpenedSkuAsync();

        using var adjust = await AdjustAsync(
            sku,
            "\"1\"",
            Guid.NewGuid().ToString(),
            new { delta = 5, reasonCode = "stocktake" },
            Collaborator
        );
        using var open = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            Collaborator,
            new { sku = NewSku() }
        );

        adjust.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        open.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
