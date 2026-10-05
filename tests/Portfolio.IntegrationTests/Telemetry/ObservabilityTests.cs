using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Portfolio.IntegrationTests.Http;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Telemetry;

/// <summary>
/// What the running API tells an operator (ai/OBSERVABILITY.md §3 to §6, §14, §17): spans named by route template and
/// attributed to a bounded context and an actor, correlation on every response, KPIs and gauges in bounded dimensions,
/// resource attributes, and, mandatory, no secret or personal data in any span, event or log record.
/// </summary>
public sealed class ObservabilityTests(TelemetryFixture fixture) : IClassFixture<TelemetryFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string Manager => fixture.TokenFor("manager").AccessToken;

    private static string NewSku() => "O-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    private static string? Tag(Activity span, string key) => span.GetTagItem(key)?.ToString();

    private static bool IsServerSpan(Activity span, string routeFragment) =>
        span.Kind == ActivityKind.Server
        && (Tag(span, "http.route")?.Contains(routeFragment, StringComparison.Ordinal) ?? false);

    // ---- Spans and correlation -------------------------------------------------------------------------------

    [Fact]
    public async Task Should_name_the_server_span_by_route_template_and_attribute_it_to_a_context_and_an_actor()
    {
        var sku = NewSku();
        using var me = await AuthClient.GetAsync(
            fixture.Client,
            "/api/v1/auth/me",
            fixture.TokenFor("collaborator").AccessToken
        );
        var actor = (await me.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();

        using var response = await AuthClient.GetAsync(
            fixture.Client,
            $"/api/v1/inventory/items/{sku}",
            fixture.TokenFor("collaborator").AccessToken
        );
        var span = await fixture.Capture.WaitForSpanAsync(
            candidate => Tag(candidate, "url.path") == $"/api/v1/inventory/items/{sku}",
            "the inventory read"
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        span.DisplayName.ShouldContain("{sku}");
        span.DisplayName.ShouldNotContain(sku);
        Tag(span, "app.bounded_context").ShouldBe("inventory");
        Tag(span, "app.actor.id").ShouldBe(actor.ToString("D"));
        Tag(span, "app.actor.type").ShouldBe("user");
        // A business refusal is an outcome, not a failure: the span is not an error (a 4xx never is).
        Tag(span, "app.outcome").ShouldBe("rejected");
        span.Status.ShouldNotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Should_not_attribute_an_anonymous_request_to_any_actor()
    {
        using var response = await fixture.Client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Cancel);
        var span = await fixture.Capture.WaitForSpanAsync(
            candidate => IsServerSpan(candidate, "api/v1/products"),
            "the product list"
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Tag(span, "app.bounded_context").ShouldBe("catalog");
        Tag(span, "app.actor.id").ShouldBeNull();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Should_not_trace_the_probes(string path)
    {
        using var probe = await fixture.Client.GetAsync(new Uri(path, UriKind.Relative), Cancel);
        using var marker = await fixture.Client.GetAsync(new Uri("/api/v1/products?limit=1", UriKind.Relative), Cancel);
        await fixture.Capture.WaitForSpanAsync(
            span => IsServerSpan(span, "api/v1/products"),
            "a request after the probe"
        );

        probe.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.Capture.Spans.ShouldNotContain(span =>
            (Tag(span, "url.path") ?? string.Empty).StartsWith("/health", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Should_answer_every_response_with_the_trace_id_as_x_request_id_and_ignore_the_callers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/products", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Request-ID", "forged-by-the-caller");

        using var response = await fixture.Client.SendAsync(request, Cancel);
        var requestId = response.Headers.GetValues("X-Request-ID").ShouldHaveSingleItem();
        var span = await fixture.Capture.WaitForSpanAsync(
            candidate => candidate.Kind == ActivityKind.Server && candidate.TraceId.ToString() == requestId,
            "the span of that request"
        );

        requestId.ShouldNotBe("forged-by-the-caller");
        requestId.Length.ShouldBe(32);
        span.TraceId.ToString().ShouldBe(requestId);
    }

    [Theory]
    [InlineData("/api/v1/auth/me", HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound)]
    public async Task Should_set_x_request_id_on_error_responses_too(string path, HttpStatusCode expected)
    {
        using var response = await fixture.Client.GetAsync(new Uri(path, UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(expected);
        response.Headers.GetValues("X-Request-ID").ShouldHaveSingleItem().Length.ShouldBe(32);
    }

    [Fact]
    public async Task Should_trace_the_database_and_the_cache_inside_the_request_without_leaking_values()
    {
        var sku = NewSku();
        using var opened = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            Manager,
            new { sku }
        );
        var server = await fixture.Capture.WaitForSpanAsync(
            span =>
                IsServerSpan(span, "inventory/items")
                && span.Kind == ActivityKind.Server
                && Tag(span, "http.request.method") == "POST",
            "the open request"
        );

        var inTrace = fixture.Capture.Spans.Where(span => span.TraceId == server.TraceId).ToArray();
        var database = inTrace.Where(span => span.Source.Name == "Npgsql").ToArray();
        var cache = inTrace.Where(span => span.Source.Name == "Ecommerce.Cache").ToArray();

        opened.StatusCode.ShouldBe(HttpStatusCode.Created);
        database.ShouldNotBeEmpty("Npgsql spans are children of the request");
        cache.ShouldNotBeEmpty("the account cache of the token check is a client span of the request");
        cache.ShouldAllBe(span =>
            span.Kind == ActivityKind.Client && Tag(span, "app.cache.name") == "identity.account"
        );
        // Statements are parameterized: the SKU is a value, and a value is never a span attribute.
        inTrace
            .SelectMany(span => span.TagObjects)
            .Select(tag => tag.Value?.ToString() ?? string.Empty)
            .ShouldAllBe(value => !value.Contains(sku, StringComparison.Ordinal));
    }

    // ---- The mandatory redaction test -------------------------------------------------------------------------

    [Fact]
    public async Task Should_never_put_a_password_a_token_or_an_email_in_any_span_event_or_log_record()
    {
        var email = $"redaction.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        var wrongPassword = ApiFactory.RandomPassword();

        using var registered = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            null,
            new { email, password }
        );
        using var rejected = await AuthClient.SignInRawAsync(fixture.Client, email, wrongPassword);
        var tokens = await AuthClient.ReadTokensAsync(await AuthClient.SignInRawAsync(fixture.Client, email, password));
        using var me = await AuthClient.GetAsync(fixture.Client, "/api/v1/auth/me", tokens.AccessToken);
        using var refreshed = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken);
        using var replay = await AuthClient.RefreshAsync(fixture.Client, tokens.RefreshToken); // reuse detection logs at Critical
        await fixture.Capture.WaitForSpanAsync(
            span => IsServerSpan(span, "auth/tokens/refresh") && Tag(span, "http.response.status_code") == "401",
            "the replay"
        );

        var secrets = new[]
        {
            password,
            wrongPassword,
            tokens.AccessToken,
            tokens.RefreshToken,
            email,
            email.Split('@')[0],
        };
        var exported = fixture.Capture.AllSpanText().Concat(fixture.Capture.Logs.Records).ToArray();

        registered.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        exported.ShouldNotBeEmpty();
        foreach (var secret in secrets)
        {
            exported.ShouldAllBe(
                text => !text.Contains(secret, StringComparison.Ordinal),
                $"'{secret[..6]}…' must not appear in telemetry"
            );
        }

        // No credential-bearing header is ever an attribute, and no body is.
        fixture
            .Capture.Spans.SelectMany(span => span.TagObjects)
            .Select(tag => tag.Key)
            .ShouldAllBe(key =>
                !key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                && !key.Contains("body", StringComparison.OrdinalIgnoreCase)
            );
    }

    // ---- Resource ---------------------------------------------------------------------------------------------

    [Fact]
    public void Should_describe_the_service_with_the_mandatory_resource_attributes()
    {
        var resource = fixture.Factory.Services.GetRequiredService<TracerProvider>().GetResource();
        var attributes = resource.Attributes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToString(),
            StringComparer.Ordinal
        );

        attributes["service.name"].ShouldBe("ecommerce-api");
        attributes["service.namespace"].ShouldBe("ecommerce");
        attributes["service.version"].ShouldNotBeNullOrWhiteSpace();
        attributes["service.instance.id"].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Should_make_every_http_latency_threshold_a_histogram_boundary()
    {
        ObservabilityExtensions.HttpDurationBoundaries.ShouldBe([
            .. ObservabilityExtensions.HttpDurationBoundaries.Order(),
        ]);
        ObservabilityExtensions.HttpDurationBoundaries.ShouldContain(0.3);
        ObservabilityExtensions.HttpDurationBoundaries.ShouldContain(0.8);
    }

    [Fact]
    public async Task Should_export_the_http_server_duration_with_the_slo_boundaries()
    {
        using var response = await fixture.Client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Cancel);
        fixture.Factory.Services.GetRequiredService<MeterProvider>().ForceFlush(10_000);

        var metric = fixture.Capture.Metrics.Last(candidate => candidate.Name == "http.server.request.duration");
        var points = metric.GetMetricPoints().GetEnumerator();
        points.MoveNext().ShouldBeTrue();
        var bounds = new List<double>();
        foreach (var bucket in points.Current.GetHistogramBuckets())
        {
            bounds.Add(bucket.ExplicitBound);
        }

        bounds.ShouldContain(0.3);
        metric.Unit.ShouldBe("s");
    }

    // ---- Business KPIs, bounded -------------------------------------------------------------------------------

    [Fact]
    public async Task Should_count_stock_adjustments_by_direction_after_the_commit_and_never_by_sku()
    {
        var factory = fixture.Factory;
        using var adjustments = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(),
            "Ecommerce.Inventory",
            "inventory.stock.adjustments"
        );
        var sku = NewSku();
        using var opened = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            Manager,
            new { sku }
        );

        await AdjustAsync(sku, "\"1\"", 10);
        await AdjustAsync(sku, "\"2\"", -3);
        // A refused adjustment (below the reserved quantity) commits nothing, so it counts nothing.
        await AdjustAsync(sku, "\"3\"", -100);

        var points = adjustments.GetMeasurementSnapshot();
        points
            .Select(point => point.Tags["inventory.adjustment.direction"]?.ToString())
            .ShouldBe(["increase", "decrease"]);
        points.ShouldAllBe(point => point.Tags["app.bounded_context"]!.ToString() == "inventory");
        points
            .SelectMany(point => point.Tags.Values)
            .ShouldAllBe(value => value == null || !value.ToString()!.Contains(sku, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_count_sign_in_outcomes_without_the_account()
    {
        using var signIns = new MetricCollector<long>(
            fixture.Factory.Services.GetRequiredService<IMeterFactory>(),
            "Ecommerce.Identity",
            "identity.signins"
        );
        var email = $"signin.{Guid.NewGuid():N}@example.com";
        var password = ApiFactory.RandomPassword();
        using var registered = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/auth/registrations",
            null,
            new { email, password }
        );

        using var failed = await AuthClient.SignInRawAsync(fixture.Client, email, ApiFactory.RandomPassword());
        using var succeeded = await AuthClient.SignInRawAsync(fixture.Client, email, password);

        signIns
            .GetMeasurementSnapshot()
            .Select(point => point.Tags["app.outcome"]?.ToString())
            .ShouldBe(["failed", "succeeded"]);
        signIns
            .GetMeasurementSnapshot()
            .SelectMany(point => point.Tags.Keys)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ShouldBe(["app.bounded_context", "app.outcome"]);
    }

    [Fact]
    public async Task Should_publish_the_outbox_backlog_gauges_of_every_context()
    {
        var sku = NewSku();
        using var opened = await AuthClient.SendJsonAsync(
            fixture.Client,
            HttpMethod.Post,
            "/api/v1/inventory/items",
            Manager,
            new { sku }
        );
        var monitor = fixture.Factory.Services.GetServices<IHostedService>().OfType<OutboxBacklogMonitor>().Single();
        using var pending = new MetricCollector<double>(
            fixture.Factory.Services.GetRequiredService<IMeterFactory>(),
            "Ecommerce.Outbox",
            "app.outbox.pending"
        );
        using var age = new MetricCollector<double>(
            fixture.Factory.Services.GetRequiredService<IMeterFactory>(),
            "Ecommerce.Outbox",
            "app.outbox.oldest_pending_age"
        );

        await monitor.SampleAsync(Cancel);
        pending.RecordObservableInstruments();
        age.RecordObservableInstruments();

        // No relay is wired, so what the API committed is still waiting: that is exactly what the gauge must show.
        var inventory = pending
            .GetMeasurementSnapshot()
            .Last(point => point.Tags["app.bounded_context"]?.ToString() == "inventory");
        inventory.Value.ShouldBeGreaterThanOrEqualTo(1);
        age.GetMeasurementSnapshot()
            .Last(point => point.Tags["app.bounded_context"]?.ToString() == "inventory")
            .Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    private async Task AdjustAsync(string sku, string ifMatch, int delta)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/api/v1/inventory/items/{sku}/adjustments", UriKind.Relative)
        )
        {
            Content = JsonContent.Create(new { delta, reasonCode = "stocktake" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Manager);
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await fixture.Client.SendAsync(request, Cancel);
    }
}
