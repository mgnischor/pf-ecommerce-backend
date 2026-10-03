using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Customers.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The Customers use cases through the real pipeline (BR-CUS-001 to BR-CUS-009): the profile a customer registers with,
/// reading it masked, and changing it with JSON Merge Patch and <c>If-Match</c>. The worker role does not run here, so each
/// test delivers the <c>CustomerRegistered</c> event the API wrote to the outbox to the real consumer, byte for byte;
/// <see cref="Messaging.CustomerRegistrationFlowTests"/> covers the broker in between.
/// </summary>
public sealed class CustomersTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string MergePatch = "application/merge-patch+json";

    private sealed record Customer(Guid Id, string Email, string AccessToken);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"customer.{Guid.NewGuid():N}@example.com";

    private async Task<Customer> RegisterAsync(object? profile = null, bool deliverEvent = true)
    {
        var email = NewEmail();
        var password = ApiFactory.RandomPassword();
        var body = new Dictionary<string, object?> { ["email"] = email, ["password"] = password };
        foreach (var property in profile?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(profile);
        }

        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            body
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var me = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        var id = (await me.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();

        if (deliverEvent)
        {
            await DeliverRegistrationEventAsync(id);
        }

        return new Customer(id, email, tokens.AccessToken);
    }

    // What the worker does: take the event from the outbox and hand its body to the consumer.
    private async Task DeliverRegistrationEventAsync(Guid accountId, bool expectHandled = true)
    {
        var payload = (
            await fixture.Factory.Database.StringsAsync(
                $"SELECT payload::text FROM identity.outbox_messages WHERE aggregate_id = '{accountId}' AND type LIKE '%CustomerRegistered'"
            )
        ).ShouldHaveSingleItem();
        var eventId = JsonDocument.Parse(payload).RootElement.GetProperty("eventId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var handled = await new CustomerRegisteredConsumer().ConsumeAsync(
            new ReceivedMessage(
                eventId,
                "identity.customer-registered",
                CorrelationId: null,
                Redelivered: false,
                Attempt: 1,
                Encoding.UTF8.GetBytes(payload)
            ),
            scope.ServiceProvider,
            Cancel
        );
        handled.ShouldBe(expectHandled);
    }

    private Task<HttpResponseMessage> GetMeAsync(string? token) =>
        AuthClient.GetAsync(fixture.Client, "/api/v1/customers/me", token);

    private async Task<HttpResponseMessage> PatchMeAsync(string? token, string json, string? ifMatch = "\"1\"")
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/v1/customers/me", UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, MergePatch),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await fixture.Client.SendAsync(request, Cancel);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    [Fact]
    [Trait("Rule", "BR-CUS-009")]
    public async Task Should_show_the_profile_the_customer_registered_with_and_mask_the_contact_data()
    {
        var customer = await RegisterAsync(
            new
            {
                fullName = "  Ana   Souza ",
                phone = "+5511987654321",
                locale = "EN",
                timeZone = "Europe/Lisbon",
            }
        );

        using var response = await GetMeAsync(customer.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        var raw = await response.Content.ReadAsStringAsync(Cancel);
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("id").GetGuid().ShouldBe(customer.Id);
        body.GetProperty("fullName").GetString().ShouldBe("Ana Souza");
        body.GetProperty("maskedEmail").GetString().ShouldBe("c***@example.com");
        body.GetProperty("maskedPhone").GetString().ShouldBe("+*********4321");
        body.GetProperty("locale").GetString().ShouldBe("en");
        body.GetProperty("timeZone").GetString().ShouldBe("Europe/Lisbon");
        body.GetProperty("version").GetInt32().ShouldBe(1);
        raw.ShouldNotContain(customer.Email);
        raw.ShouldNotContain("5511987654321");
    }

    [Fact]
    [Trait("Rule", "BR-CUS-006")]
    public async Task Should_give_a_customer_who_typed_nothing_a_profile_with_the_defaults()
    {
        var customer = await RegisterAsync();

        using var response = await GetMeAsync(customer.AccessToken);

        var body = await ReadAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("fullName").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("maskedPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("locale").GetString().ShouldBe("pt-BR");
        body.GetProperty("timeZone").GetString().ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    [Trait("Rule", "BR-CUS-006")]
    public async Task Should_register_the_customer_and_leave_out_only_the_optional_data_that_breaks_a_rule()
    {
        var customer = await RegisterAsync(
            new
            {
                fullName = "1234",
                phone = "11987654321",
                locale = "fr-FR",
                timeZone = "Europe/Lisbon",
            }
        );

        using var response = await GetMeAsync(customer.AccessToken);

        var body = await ReadAsync(response);
        body.GetProperty("fullName").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("maskedPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("locale").GetString().ShouldBe("pt-BR");
        body.GetProperty("timeZone").GetString().ShouldBe("Europe/Lisbon");
    }

    [Fact]
    [Trait("Rule", "BR-CUS-007")]
    public async Task Should_answer_404_for_an_account_that_has_no_profile_such_as_staff()
    {
        using var response = await GetMeAsync(fixture.TokenFor("manager").AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("CUSTOMER_PROFILE_NOT_FOUND");
    }

    [Fact]
    [Trait("Rule", "BR-CUS-007")]
    public async Task Should_answer_404_until_the_profile_is_created_after_registration()
    {
        var customer = await RegisterAsync(deliverEvent: false);

        using var before = await GetMeAsync(customer.AccessToken);
        await DeliverRegistrationEventAsync(customer.Id);
        using var after = await GetMeAsync(customer.AccessToken);

        before.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-007")]
    public async Task Should_let_each_customer_reach_only_their_own_profile_and_expose_no_identifier_route()
    {
        var ana = await RegisterAsync(new { fullName = "Ana Souza" });
        var bruno = await RegisterAsync(new { fullName = "Bruno Lima" });

        var anaBody = await ReadAsync(await GetMeAsync(ana.AccessToken));
        var brunoBody = await ReadAsync(await GetMeAsync(bruno.AccessToken));
        using var byId = await AuthClient.GetAsync(fixture.Client, $"/api/v1/customers/{bruno.Id}", ana.AccessToken);

        anaBody.GetProperty("id").GetGuid().ShouldBe(ana.Id);
        anaBody.GetProperty("fullName").GetString().ShouldBe("Ana Souza");
        brunoBody.GetProperty("id").GetGuid().ShouldBe(bruno.Id);
        byId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-008")]
    public async Task Should_change_only_the_members_sent_and_advance_the_etag()
    {
        var customer = await RegisterAsync(new { fullName = "Ana Souza", phone = "+5511987654321" });

        using var patched = await PatchMeAsync(
            customer.AccessToken,
            """{ "fullName": "Ana Maria Souza", "locale": "en" }"""
        );
        using var read = await GetMeAsync(customer.AccessToken);

        patched.StatusCode.ShouldBe(HttpStatusCode.OK);
        patched.Headers.ETag?.Tag.ShouldBe("\"2\"");
        var body = await ReadAsync(read);
        body.GetProperty("fullName").GetString().ShouldBe("Ana Maria Souza");
        body.GetProperty("locale").GetString().ShouldBe("en");
        body.GetProperty("maskedPhone").GetString().ShouldBe("+*********4321");
        body.GetProperty("timeZone").GetString().ShouldBe("America/Sao_Paulo");
        body.GetProperty("version").GetInt32().ShouldBe(2);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-008")]
    public async Task Should_remove_the_phone_when_sent_as_null_and_keep_it_when_omitted()
    {
        var customer = await RegisterAsync(new { phone = "+5511987654321" });

        using var omitted = await PatchMeAsync(customer.AccessToken, """{ "locale": "en" }""");
        using var removed = await PatchMeAsync(customer.AccessToken, """{ "phone": null }""", "\"2\"");

        (await ReadAsync(omitted)).GetProperty("maskedPhone").GetString().ShouldBe("+*********4321");
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(removed)).GetProperty("maskedPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        removed.Headers.ETag?.Tag.ShouldBe("\"3\"");
    }

    [Theory]
    [Trait("Rule", "BR-CUS-008")]
    [InlineData("{}")]
    [InlineData("""{ "locale": "pt-br" }""")]
    public async Task Should_leave_the_etag_alone_when_the_patch_changes_nothing(string json)
    {
        var customer = await RegisterAsync();

        using var response = await PatchMeAsync(customer.AccessToken, json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        (await ReadAsync(response)).GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-008")]
    public async Task Should_answer_the_same_state_when_the_same_patch_is_repeated_with_the_new_etag()
    {
        var customer = await RegisterAsync();
        const string Json = """{ "fullName": "Ana Souza" }""";

        using var first = await PatchMeAsync(customer.AccessToken, Json);
        using var repeat = await PatchMeAsync(customer.AccessToken, Json, first.Headers.ETag?.Tag);

        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        repeat.Headers.ETag?.Tag.ShouldBe("\"2\"");
    }

    [Theory]
    [Trait("Rule", "BR-CUS-008")]
    [InlineData("\"7\"")]
    [InlineData("\"0\"")]
    [InlineData("1")]
    [InlineData("*")]
    [InlineData("garbage")]
    public async Task Should_answer_412_for_a_stale_or_malformed_if_match_and_change_nothing(string ifMatch)
    {
        var customer = await RegisterAsync(new { fullName = "Ana Souza" });

        using var response = await PatchMeAsync(customer.AccessToken, """{ "fullName": "Outro Nome" }""", ifMatch);
        var read = await ReadAsync(await GetMeAsync(customer.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("CUSTOMER_VERSION_MISMATCH");
        read.GetProperty("fullName").GetString().ShouldBe("Ana Souza");
        read.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-008")]
    public async Task Should_answer_400_when_the_if_match_header_is_missing()
    {
        var customer = await RegisterAsync();

        using var response = await PatchMeAsync(customer.AccessToken, """{ "locale": "en" }""", ifMatch: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("""{ "fullName": "A" }""", "CUSTOMER_FULL_NAME_LENGTH", "/fullName", "BR-CUS-001")]
    [InlineData("""{ "fullName": "1234" }""", "CUSTOMER_FULL_NAME_INVALID", "/fullName", "BR-CUS-001")]
    [InlineData("""{ "fullName": null }""", "CUSTOMER_FULL_NAME_REQUIRED", "/fullName", "BR-CUS-001")]
    [InlineData("""{ "phone": "11987654321" }""", "CUSTOMER_PHONE_INVALID", "/phone", "BR-CUS-003")]
    [InlineData("""{ "locale": "fr-FR" }""", "CUSTOMER_LOCALE_UNSUPPORTED", "/locale", "BR-CUS-004")]
    [InlineData("""{ "locale": null }""", "CUSTOMER_LOCALE_REQUIRED", "/locale", "BR-CUS-004")]
    [InlineData("""{ "timeZone": "Mars/Olympus" }""", "CUSTOMER_TIME_ZONE_INVALID", "/timeZone", "BR-CUS-005")]
    [InlineData("""{ "timeZone": null }""", "CUSTOMER_TIME_ZONE_REQUIRED", "/timeZone", "BR-CUS-005")]
    public async Task Should_answer_422_with_the_field_and_the_rule_for_an_invalid_member_and_apply_nothing(
        string json,
        string code,
        string field,
        string ruleId
    )
    {
        var customer = await RegisterAsync(new { fullName = "Ana Souza" });

        using var response = await PatchMeAsync(customer.AccessToken, json);
        var read = await ReadAsync(await GetMeAsync(customer.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = (await ReadAsync(response)).GetProperty("errors")[0];
        error.GetProperty("code").GetString().ShouldBe(code);
        error.GetProperty("field").GetString().ShouldBe(field);
        error.GetProperty("ruleId").GetString().ShouldBe(ruleId);
        read.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-008")]
    public async Task Should_ignore_members_the_client_must_not_change_such_as_the_email_the_id_and_the_version()
    {
        var customer = await RegisterAsync(new { fullName = "Ana Souza" });

        using var response = await PatchMeAsync(
            customer.AccessToken,
            $$"""{ "email": "attacker@example.com", "maskedEmail": "x***@example.com", "id": "{{Guid.NewGuid()}}", "version": 99 }"""
        );
        var body = await ReadAsync(await GetMeAsync(customer.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("id").GetGuid().ShouldBe(customer.Id);
        body.GetProperty("maskedEmail").GetString().ShouldBe("c***@example.com");
        body.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Should_answer_404_to_a_patch_from_an_account_without_a_profile()
    {
        using var response = await PatchMeAsync(fixture.TokenFor("developer").AccessToken, """{ "locale": "en" }""");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthClient.ReadCodeAsync(response)).ShouldBe("CUSTOMER_PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task Should_not_apply_a_patch_sent_with_a_media_type_the_operation_does_not_consume()
    {
        var customer = await RegisterAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/v1/customers/me", UriKind.Relative))
        {
            Content = new StringContent("""{ "locale": "en" }""", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        using var response = await fixture.Client.SendAsync(request, Cancel);

        // Routing rejects it before the action (the fallback route answers 404 where a bare [Consumes] would say 415).
        ((int)response.StatusCode).ShouldBeInRange(400, 499);
        (await ReadAsync(await GetMeAsync(customer.AccessToken))).GetProperty("locale").GetString().ShouldBe("pt-BR");
    }

    [Fact]
    public async Task Should_require_authentication_for_both_operations()
    {
        using var get = await GetMeAsync(token: null);
        using var patch = await PatchMeAsync(token: null, """{ "locale": "en" }""");

        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        patch.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-006")]
    public async Task Should_keep_the_profile_of_a_customer_created_twice_by_a_redelivered_event()
    {
        var customer = await RegisterAsync(new { fullName = "Ana Souza" });
        await PatchMeAsync(customer.AccessToken, """{ "fullName": "Ana Maria Souza" }""");

        await DeliverRegistrationEventAsync(customer.Id, expectHandled: false);
        var body = await ReadAsync(await GetMeAsync(customer.AccessToken));

        body.GetProperty("fullName").GetString().ShouldBe("Ana Maria Souza");
        body.GetProperty("version").GetInt32().ShouldBe(2);
        (
            await fixture.Factory.Database.ScalarAsync(
                $"SELECT count(*) FROM customers.customer_profiles WHERE id = '{customer.Id}'"
            )
        ).ShouldBe(1L);
    }
}
