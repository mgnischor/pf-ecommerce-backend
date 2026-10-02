using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portfolio.IntegrationTests.Caching;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// The API on top of Valkey (ai/DATABASE.md §4.2): the account state a token check needs is cached under a short TTL and
/// evicted by the event that changes it; sign-out blocks the token in Valkey; and the platform keeps (or fails closed)
/// through a Valkey outage as documented.
/// </summary>
public sealed class ValkeyApiTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private CacheHost Inspector => new(prefix: fixture.Factory.ValkeyKeyPrefix);

    private async Task<(Guid Id, string Token)> SignedInManagerAsync()
    {
        using var me = await AuthClient.GetAsync(
            fixture.Client,
            "/api/v1/auth/me",
            fixture.TokenFor("manager").AccessToken
        );
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return (profile.GetProperty("id").GetGuid(), fixture.TokenFor("manager").AccessToken);
    }

    [Fact]
    public async Task Should_cache_the_account_state_under_a_versioned_key_with_a_short_ttl_and_nothing_personal()
    {
        var (id, _) = await SignedInManagerAsync();
        await using var inspector = Inspector;

        var key = $"{fixture.Factory.ValkeyKeyPrefix}:identity:account:{id:N}:v1";
        var stored = await inspector.InspectAsync(key);

        stored.Ttl.ShouldNotBeNull().TotalSeconds.ShouldBeInRange(1, 30);
        // HybridCache wraps the payload in a small binary header and footer; the JSON body is between them.
        var payload = stored.Value.ShouldNotBeNull();
        using var value = JsonDocument.Parse(
            payload[payload.IndexOf('{', StringComparison.Ordinal)..(payload.LastIndexOf('}') + 1)]
        );
        value
            .RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Exists", "Level", "Status", "TokenVersion"]);
        // No e-mail, no hash, no name: the entry holds exactly what a token check needs.
        payload.ShouldNotContain("@");
        payload.ShouldNotContain("argon2", Case.Insensitive);
    }

    [Fact]
    public async Task Should_evict_the_cached_account_the_moment_it_is_deactivated_and_refuse_its_token()
    {
        var email = $"cache.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/users",
            fixture.TokenFor("administrator").AccessToken,
            new
            {
                email,
                password,
                accessLevel = "collaborator",
            }
        );
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var warm = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        await using var inspector = Inspector;
        var key = $"{fixture.Factory.ValkeyKeyPrefix}:identity:account:{id:N}:v1";
        (await inspector.InspectAsync(key)).Value.ShouldNotBeNull();

        using var deactivation = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            $"/api/v1/users/{id}/deactivation",
            fixture.TokenFor("administrator").AccessToken
        );
        using var afterwards = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);

        warm.StatusCode.ShouldBe(HttpStatusCode.OK);
        deactivation.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Not "within 30 seconds": at once, because the event evicted the entry after the commit.
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_block_a_signed_out_token_in_valkey_for_the_rest_of_its_life()
    {
        var email = $"logout.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email, password }
        );
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));

        using var signOut = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/tokens/revocation",
            tokens.AccessToken,
            new { refreshToken = tokens.RefreshToken }
        );
        using var reuse = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await using var inspector = Inspector;
        var blocked = (await inspector.KeysAsync())
            .Where(key => key.Contains(":identity:revoked-jti:", StringComparison.Ordinal))
            .ToArray();
        blocked.ShouldHaveSingleItem();
        (await inspector.InspectAsync(blocked[0])).Ttl.ShouldNotBeNull().TotalSeconds.ShouldBeInRange(1, 15 * 60);
    }

    [Fact]
    public async Task Should_refuse_an_access_token_at_once_when_a_replayed_refresh_token_revokes_the_session()
    {
        var email = $"replay.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var created = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            accessToken: null,
            new { email, password }
        );
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var warm = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);

        using var rotated = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);
        using var replay = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);
        using var afterwards = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);

        warm.StatusCode.ShouldBe(HttpStatusCode.OK);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // The reuse bumped the token version and the UserTokensRevoked event evicted the cached account: not "within 30 s".
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_serve_authenticated_requests_from_the_database_when_valkey_is_down_and_the_operator_allows_it()
    {
        using var factory = new ApiFactory(
            "Production",
            configure: builder =>
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>(StringComparer.Ordinal)
                            {
                                ["Valkey:RevocationCheckFailureMode"] = "Allow",
                            }
                        )
                ),
            valkeyConnectionString: "127.0.0.1:1,password=irrelevant"
        );
        using var client = factory.CreateClient();
        var tokens = await AuthClient.SignInAsync(client, factory.Accounts["manager"]);

        using var me = await AuthClient.GetAsync(client, "/api/v1/auth/me", tokens.AccessToken);
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Degraded, not down: the instance stays in the load balancer.
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync(Cancel)).ShouldBe("Degraded");
    }

    [Fact]
    public async Task Should_refuse_authenticated_requests_while_valkey_is_down_by_default_but_keep_anonymous_ones_working()
    {
        using var factory = new ApiFactory("Production", valkeyConnectionString: "127.0.0.1:1,password=irrelevant");
        using var client = factory.CreateClient();
        var tokens = await AuthClient.SignInAsync(client, factory.Accounts["manager"]);

        using var me = await AuthClient.GetAsync(client, "/api/v1/auth/me", tokens.AccessToken);
        using var catalog = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Cancel);

        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        catalog.StatusCode.ShouldBe(HttpStatusCode.NotImplemented); // Reached the endpoint: no cache or blocklist involved.
    }

    [Fact]
    public async Task Should_report_ready_and_healthy_while_valkey_is_reachable()
    {
        using var ready = await fixture.Client.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync(Cancel)).ShouldBe("Healthy");
    }
}
