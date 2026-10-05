using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The Catalog use cases through the real pipeline and a real PostgreSQL: creation and deletion with
/// <c>Idempotency-Key</c>, maintenance with <c>If-Match</c>, visibility by access level, and cursor pagination
/// (BR-CAT-001 to BR-CAT-008).
/// </summary>
public sealed class CatalogTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string Json = "application/json";
    private const string MergePatch = "application/merge-patch+json";

    private string Collaborator => fixture.TokenFor("collaborator").AccessToken;

    private string Manager => fixture.TokenFor("manager").AccessToken;

    private string Administrator => fixture.TokenFor("administrator").AccessToken;

    private string Customer => fixture.TokenFor("public").AccessToken;

    private static string NewSku() => "T-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    private static string NewKey() => Guid.NewGuid().ToString();

    // A text no other test uses, so a search finds only the products a test created.
    private static string NewToken() => "zq" + Guid.NewGuid().ToString("N")[..12];

    private static object NewProduct(
        string? sku = null,
        string name = "Cafeteira Elétrica",
        string amount = "189.90"
    ) =>
        new
        {
            name,
            sku = sku ?? NewSku(),
            price = new { amount, currency = "BRL" },
            description = "Com filtro permanente.",
        };

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string? token,
        object? body = null,
        string? ifMatch = null,
        string? key = null,
        string mediaType = Json
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        if (body is not null)
        {
            var content = body as string ?? JsonSerializer.Serialize(body);
            request.Content = new StringContent(content, Encoding.UTF8, mediaType);
        }

        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> CreateAsync(object? body = null, string? key = null, string? token = null) =>
        SendAsync(
            HttpMethod.Post,
            "/api/v1/products",
            token ?? Collaborator,
            body ?? NewProduct(),
            key: key ?? NewKey()
        );

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private async Task<(Guid Id, string Sku)> CreatedAsync(
        string name = "Cafeteira Elétrica",
        string amount = "189.90",
        bool activate = false
    )
    {
        var sku = NewSku();
        using var created = await CreateAsync(NewProduct(sku, name, amount));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        if (activate)
        {
            using var activated = await SendAsync(
                HttpMethod.Post,
                $"/api/v1/products/{id}/activation",
                Manager,
                ifMatch: "\"1\""
            );
            activated.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return (id, sku);
    }

    private Task<HttpResponseMessage> ListAsync(string query, string? token = null) =>
        SendAsync(HttpMethod.Get, "/api/v1/products" + query, token);

    private static string[] NamesOf(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];

    // --- Creation (BR-CAT-001, BR-CAT-002, BR-CAT-005, BR-CAT-008) ---

    [Fact]
    public async Task Should_create_a_draft_product_with_its_location_etag_and_allowed_actions()
    {
        var sku = NewSku();

        using var response = await CreateAsync(NewProduct(sku.ToLowerInvariant()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location?.OriginalString.ShouldBe($"/api/v1/products/{id}");
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        body.GetProperty("sku").GetString().ShouldBe(sku);
        body.GetProperty("status").GetString().ShouldBe("draft");
        body.GetProperty("version").GetInt32().ShouldBe(1);
        body.GetProperty("price").GetProperty("amount").GetString().ShouldBe("189.90");
        body.GetProperty("price").GetProperty("currency").GetString().ShouldBe("BRL");
        // Instants are RFC 3339 UTC with a trailing Z (ai/API_CONTRACTS.md §3).
        body.GetProperty("createdAt").GetString().ShouldEndWith("Z");
        body.GetProperty("updatedAt").GetString().ShouldEndWith("Z");
        body.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldContain("activate");
    }

    [Fact]
    public async Task Should_answer_a_retried_creation_with_the_original_product_and_create_nothing_more()
    {
        var key = NewKey();
        var body = NewProduct();
        using var first = await CreateAsync(body, key);

        using var retry = await CreateAsync(body, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadAsync(retry))
            .GetProperty("id")
            .GetGuid()
            .ShouldBe((await ReadAsync(first)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Should_refuse_reusing_an_idempotency_key_for_a_different_product_with_a_422()
    {
        var key = NewKey();
        using var first = await CreateAsync(NewProduct(), key);

        using var reuse = await CreateAsync(NewProduct(), key);

        reuse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(reuse)).ShouldBe("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task Should_refuse_a_second_product_with_the_same_sku_even_under_another_key()
    {
        var sku = NewSku();
        using var first = await CreateAsync(NewProduct(sku));

        using var second = await CreateAsync(NewProduct(sku.ToLowerInvariant()));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(second)).ShouldBe("PRODUCT_SKU_ALREADY_EXISTS");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public async Task Should_answer_400_when_the_idempotency_key_is_missing_or_too_short(string? key)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/products", Collaborator, NewProduct(), key: key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_answer_422_for_a_price_that_is_not_positive()
    {
        using var response = await CreateAsync(NewProduct(amount: "0.00"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
    }

    [Theory]
    [InlineData("12,50")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("12.99999")]
    public async Task Should_answer_400_for_an_amount_that_is_not_a_decimal_string(string amount)
    {
        using var response = await CreateAsync(NewProduct(amount: amount));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Visibility (BR-CAT-006) ---

    [Fact]
    public async Task Should_hide_a_draft_product_from_the_public_and_show_it_to_staff()
    {
        var (id, _) = await CreatedAsync();

        using var anonymous = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", token: null);
        using var customer = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Customer);
        using var staff = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Collaborator);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        customer.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(anonymous)).ShouldBe("PRODUCT_NOT_FOUND");
        staff.StatusCode.ShouldBe(HttpStatusCode.OK);
        staff.Headers.ETag?.Tag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Should_show_an_active_product_to_everyone()
    {
        var (id, _) = await CreatedAsync(activate: true);

        using var anonymous = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", token: null);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.OK);
        anonymous.Headers.ETag?.Tag.ShouldBe("\"2\"");
        (await ReadAsync(anonymous)).GetProperty("status").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task Should_answer_404_for_a_product_that_does_not_exist()
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/products/{Guid.NewGuid()}", Collaborator);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_NOT_FOUND");
    }

    // --- Listing, search, sorting and pagination (BR-CAT-006) ---

    [Fact]
    public async Task Should_list_only_active_products_to_the_public_and_every_status_to_staff()
    {
        var token = NewToken();
        await CreatedAsync($"{token} ativo", activate: true);
        await CreatedAsync($"{token} rascunho");

        using var anonymous = await ListAsync($"?q={token}");
        using var staff = await ListAsync($"?q={token}", Collaborator);
        using var staffDrafts = await ListAsync($"?q={token}&status=draft", Collaborator);
        using var publicDrafts = await ListAsync($"?q={token}&status=draft");

        NamesOf(await ReadAsync(anonymous)).ShouldBe([$"{token} ativo"]);
        NamesOf(await ReadAsync(staff)).Order().ShouldBe([$"{token} ativo", $"{token} rascunho"]);
        NamesOf(await ReadAsync(staffDrafts)).ShouldBe([$"{token} rascunho"]);
        NamesOf(await ReadAsync(publicDrafts)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_answer_an_empty_page_with_200_when_nothing_matches()
    {
        using var response = await ListAsync($"?q={NewToken()}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await ReadAsync(response);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Should_walk_every_page_in_order_without_duplicates_or_gaps()
    {
        var token = NewToken();
        foreach (var letter in new[] { "c", "a", "e", "b", "d" })
        {
            await CreatedAsync($"{token} {letter}", activate: true);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = $"?q={token}&sort=name&limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            using var response = await ListAsync(query);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var page = await ReadAsync(response);
            seen.AddRange(NamesOf(page));
            cursor = page.GetProperty("nextCursor").GetString();
            page.GetProperty("hasMore").GetBoolean().ShouldBe(cursor is not null);
            pages++;
        } while (cursor is not null && pages < 10);

        pages.ShouldBe(3);
        seen.ShouldBe([$"{token} a", $"{token} b", $"{token} c", $"{token} d", $"{token} e"]);
    }

    [Fact]
    public async Task Should_keep_pages_stable_when_products_share_the_sort_value()
    {
        var token = NewToken();
        for (var i = 0; i < 5; i++)
        {
            await CreatedAsync($"{token} igual", "50.00", activate: true);
        }

        var ids = new List<string>();
        string? cursor = null;
        do
        {
            using var response = await ListAsync(
                $"?q={token}&sort=price&limit=2" + (cursor is null ? "" : $"&cursor={cursor}")
            );
            var page = await ReadAsync(response);
            ids.AddRange(
                page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)
            );
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        ids.Count.ShouldBe(5);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(5);
    }

    [Fact]
    public async Task Should_sort_by_price_in_either_direction()
    {
        var token = NewToken();
        await CreatedAsync($"{token} caro", "300.00", activate: true);
        await CreatedAsync($"{token} barato", "10.00", activate: true);
        await CreatedAsync($"{token} medio", "100.00", activate: true);

        using var ascending = await ListAsync($"?q={token}&sort=price");
        using var descending = await ListAsync($"?q={token}&sort=-price");

        NamesOf(await ReadAsync(ascending)).ShouldBe([$"{token} barato", $"{token} medio", $"{token} caro"]);
        NamesOf(await ReadAsync(descending)).ShouldBe([$"{token} caro", $"{token} medio", $"{token} barato"]);
    }

    [Fact]
    public async Task Should_list_newest_first_by_default()
    {
        var token = NewToken();
        await CreatedAsync($"{token} primeiro", activate: true);
        await CreatedAsync($"{token} segundo", activate: true);

        using var response = await ListAsync($"?q={token}");

        NamesOf(await ReadAsync(response)).ShouldBe([$"{token} segundo", $"{token} primeiro"]);
    }

    [Fact]
    public async Task Should_match_the_search_text_literally_and_not_as_a_pattern()
    {
        var token = NewToken();
        await CreatedAsync($"{token} comum", activate: true);

        using var wildcard = await ListAsync("?q=%25%25");
        using var underscore = await ListAsync("?q=__");

        NamesOf(await ReadAsync(wildcard)).ShouldNotContain($"{token} comum");
        NamesOf(await ReadAsync(underscore)).ShouldNotContain($"{token} comum");
    }

    [Fact]
    public async Task Should_find_a_product_by_its_exact_sku()
    {
        var (id, sku) = await CreatedAsync(activate: true);

        using var response = await ListAsync($"?q={sku.ToLowerInvariant()}");

        var items = (await ReadAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("id").GetGuid()).ShouldBe([id]);
    }

    [Theory]
    [InlineData("?sort=sku", "SORT_FIELD_NOT_ALLOWED")]
    [InlineData("?sort=-id", "SORT_FIELD_NOT_ALLOWED")]
    [InlineData("?cursor=bm90LWEtY3Vyc29y.AAAA", "PAGE_CURSOR_INVALID")]
    [InlineData("?cursor=garbage", "PAGE_CURSOR_INVALID")]
    public async Task Should_answer_400_with_a_stable_code_for_an_unknown_sort_field_or_a_forged_cursor(
        string query,
        string code
    )
    {
        using var response = await ListAsync(query);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe(code);
    }

    [Fact]
    public async Task Should_refuse_a_cursor_when_the_sort_or_the_filters_changed()
    {
        var token = NewToken();
        foreach (var letter in new[] { "a", "b", "c" })
        {
            await CreatedAsync($"{token} {letter}", activate: true);
        }

        using var first = await ListAsync($"?q={token}&sort=name&limit=1");
        var cursor = (await ReadAsync(first)).GetProperty("nextCursor").GetString();

        using var otherSort = await ListAsync($"?q={token}&sort=price&limit=1&cursor={cursor}");
        using var otherSearch = await ListAsync($"?q={token}x&sort=name&limit=1&cursor={cursor}");

        otherSort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        otherSearch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Maintenance (BR-CAT-001, BR-CAT-002) ---

    [Fact]
    public async Task Should_update_the_name_leave_the_omitted_description_and_advance_the_etag()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            """{"name":"Cafeteira Premium"}""",
            ifMatch: "\"1\"",
            mediaType: MergePatch
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(response);
        body.GetProperty("name").GetString().ShouldBe("Cafeteira Premium");
        body.GetProperty("description").GetString().ShouldBe("Com filtro permanente.");
        body.GetProperty("version").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Should_clear_the_description_when_it_is_named_null()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            """{"description":null}""",
            ifMatch: "\"1\"",
            mediaType: MergePatch
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // An absent optional field is omitted, never returned as null or an empty string (ai/API_CONTRACTS.md §3).
        (await ReadAsync(response))
            .TryGetProperty("description", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Should_not_advance_the_version_when_the_update_changes_nothing()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            "{}",
            ifMatch: "\"1\"",
            mediaType: MergePatch
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Should_answer_422_for_an_explicit_null_name()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            """{"name":null}""",
            ifMatch: "\"1\"",
            mediaType: MergePatch
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_NAME_REQUIRED");
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("W/\"7\"")]
    [InlineData("\"abc\"")]
    [InlineData("*")]
    [InlineData("1")]
    public async Task Should_answer_412_when_if_match_is_stale_or_malformed_and_change_nothing(string ifMatch)
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            """{"name":"Outro nome"}""",
            ifMatch,
            mediaType: MergePatch
        );
        using var read = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Collaborator);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_VERSION_MISMATCH");
        (await ReadAsync(read)).GetProperty("name").GetString().ShouldBe("Cafeteira Elétrica");
    }

    [Fact]
    public async Task Should_answer_400_when_if_match_is_missing()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Patch,
            $"/api/v1/products/{id}",
            Collaborator,
            """{"name":"Outro nome"}""",
            mediaType: MergePatch
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_change_the_price_as_a_manager_and_answer_a_retry_with_the_same_price_as_a_success()
    {
        var (id, _) = await CreatedAsync();
        var body = new { price = new { amount = "159.90", currency = "BRL" } };

        using var first = await SendAsync(HttpMethod.Put, $"/api/v1/products/{id}/price", Manager, body, "\"1\"");
        using var retry = await SendAsync(HttpMethod.Put, $"/api/v1/products/{id}/price", Manager, body, "\"1\"");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag?.Tag.ShouldBe("\"2\"");
        (await ReadAsync(first)).GetProperty("price").GetProperty("amount").GetString().ShouldBe("159.90");
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.Headers.ETag?.Tag.ShouldBe("\"2\"");
    }

    [Fact]
    public async Task Should_answer_412_for_a_price_change_on_a_stale_version_and_keep_the_price()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Put,
            $"/api/v1/products/{id}/price",
            Manager,
            new { price = new { amount = "10.00", currency = "BRL" } },
            "\"9\""
        );
        using var read = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Manager);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ReadAsync(read)).GetProperty("price").GetProperty("amount").GetString().ShouldBe("189.90");
    }

    [Fact]
    public async Task Should_answer_422_for_a_price_change_to_zero()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Put,
            $"/api/v1/products/{id}/price",
            Manager,
            new { price = new { amount = "0", currency = "BRL" } },
            "\"1\""
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
    }

    // --- Lifecycle (BR-CAT-003) ---

    [Fact]
    public async Task Should_activate_a_draft_product_and_then_refuse_activating_it_again()
    {
        var (id, _) = await CreatedAsync();

        using var activated = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{id}/activation",
            Manager,
            ifMatch: "\"1\""
        );
        using var staleRetry = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{id}/activation",
            Manager,
            ifMatch: "\"1\""
        );
        using var freshRetry = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{id}/activation",
            Manager,
            ifMatch: "\"2\""
        );

        activated.StatusCode.ShouldBe(HttpStatusCode.OK);
        activated.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(activated);
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ShouldContain("discontinue");
        staleRetry.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        freshRetry.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(freshRetry)).ShouldBe("PRODUCT_INVALID_STATUS_TRANSITION");
    }

    [Fact]
    public async Task Should_refuse_discontinuing_a_draft_product()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{id}/discontinuation",
            Manager,
            ifMatch: "\"1\""
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("PRODUCT_INVALID_STATUS_TRANSITION");
    }

    [Fact]
    public async Task Should_discontinue_an_active_product_and_withdraw_it_from_the_public()
    {
        var (id, _) = await CreatedAsync(activate: true);

        using var discontinued = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{id}/discontinuation",
            Manager,
            ifMatch: "\"2\""
        );
        using var anonymous = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", token: null);
        using var staff = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Collaborator);

        discontinued.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(discontinued)).GetProperty("status").GetString().ShouldBe("discontinued");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        staff.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_answer_404_for_a_lifecycle_change_on_a_product_that_does_not_exist()
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/products/{Guid.NewGuid()}/activation",
            Manager,
            ifMatch: "\"1\""
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Deletion (BR-CAT-007, BR-CAT-008) ---

    [Fact]
    public async Task Should_logically_delete_a_product_release_its_sku_and_answer_a_retry_as_a_success()
    {
        var (id, sku) = await CreatedAsync(activate: true);
        var key = NewKey();

        using var deleted = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: "\"2\"",
            key: key
        );
        using var retry = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: "\"2\"",
            key: key
        );
        using var read = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Administrator);
        using var reused = await CreateAsync(NewProduct(sku));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        retry.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        reused.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Should_answer_404_for_a_deletion_repeated_under_another_key()
    {
        var (id, _) = await CreatedAsync();
        using var deleted = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: "\"1\"",
            key: NewKey()
        );

        using var other = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: "\"1\"",
            key: NewKey()
        );

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_answer_412_and_keep_the_product_when_the_deletion_carries_a_stale_version()
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: "\"9\"",
            key: NewKey()
        );
        using var read = await SendAsync(HttpMethod.Get, $"/api/v1/products/{id}", Administrator);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Should_answer_400_when_a_deletion_lacks_its_key_or_its_precondition(
        bool withKey,
        bool withIfMatch
    )
    {
        var (id, _) = await CreatedAsync();

        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Administrator,
            ifMatch: withIfMatch ? "\"1\"" : null,
            key: withKey ? NewKey() : null
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Authorization of the operations the matrix cannot exercise with a real body ---

    [Fact]
    public async Task Should_refuse_a_manager_the_deletion_and_a_collaborator_the_price_change()
    {
        var (id, _) = await CreatedAsync();

        using var delete = await SendAsync(
            HttpMethod.Delete,
            $"/api/v1/products/{id}",
            Manager,
            ifMatch: "\"1\"",
            key: NewKey()
        );
        using var price = await SendAsync(
            HttpMethod.Put,
            $"/api/v1/products/{id}/price",
            Collaborator,
            new { price = new { amount = "10.00", currency = "BRL" } },
            "\"1\""
        );

        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        price.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
