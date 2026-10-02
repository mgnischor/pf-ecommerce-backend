using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Caching;

/// <summary>
/// The cache boundary against a real Valkey (ai/DATABASE.md §4.2, ai/TESTS.md §4.1): values live under versioned keys
/// with a TTL, a stampede computes once, a failure of the source of truth reaches the caller once, and an outage of
/// Valkey is invisible to the caller except in the signals.
/// </summary>
public sealed class ResilientCacheTests
{
    private const string Unreachable = "127.0.0.1:1,password=irrelevant";

    private static readonly CacheEntry Entry = new("test.thing", "test", "thing", 1, TimeSpan.FromSeconds(30));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_compute_once_and_serve_the_second_read_from_valkey()
    {
        await using var host = new CacheHost();
        var calls = 0;

        var first = await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);
        var second = await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);

        (first, second, calls).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Should_store_the_value_under_a_namespaced_versioned_key_with_the_entrys_ttl()
    {
        await using var host = new CacheHost();
        await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult("payload"), Cancel);

        var keys = await host.KeysAsync();
        var stored = await host.InspectAsync(keys.ShouldHaveSingleItem());

        keys[0].ShouldBe($"{host.Prefix}:test:thing:abc:v1");
        stored.Ttl.ShouldNotBeNull().TotalSeconds.ShouldBeInRange(1, 30);
        stored.Value.ShouldNotBeNull().ShouldContain("payload");
    }

    [Fact]
    public async Task Should_let_an_entry_expire_and_compute_it_again()
    {
        await using var host = new CacheHost();
        var shortLived = Entry with { Ttl = TimeSpan.FromSeconds(1) };
        var calls = 0;
        await host.Cache.GetOrCreateAsync(shortLived, "abc", _ => Task.FromResult(++calls), Cancel);

        await Task.Delay(TimeSpan.FromMilliseconds(1_600), Cancel);
        var again = await host.Cache.GetOrCreateAsync(shortLived, "abc", _ => Task.FromResult(++calls), Cancel);

        again.ShouldBe(2);
    }

    [Fact]
    public async Task Should_evict_a_value_so_every_reader_computes_it_again()
    {
        await using var writer = new CacheHost();
        await using var otherInstance = new CacheHost(prefix: writer.Prefix);
        var calls = 0;
        await writer.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);
        await otherInstance.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);

        await writer.Cache.RemoveAsync(Entry, "abc", Cancel);
        var afterEviction = await otherInstance.Cache.GetOrCreateAsync(
            Entry,
            "abc",
            _ => Task.FromResult(++calls),
            Cancel
        );

        // No in-process copy: the other instance cannot keep serving what was evicted.
        afterEviction.ShouldBe(2);
    }

    [Fact]
    public async Task Should_compute_once_when_many_callers_ask_for_the_same_missing_key_at_once()
    {
        await using var host = new CacheHost();
        var calls = 0;

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 25)
                .Select(_ =>
                    host.Cache.GetOrCreateAsync(
                        Entry,
                        "hot",
                        async token =>
                        {
                            await Task.Delay(150, token);
                            return Interlocked.Increment(ref calls);
                        },
                        Cancel
                    )
                )
        );

        calls.ShouldBe(1);
        results.ShouldAllBe(value => value == 1);
    }

    [Fact]
    public async Task Should_let_a_failure_of_the_source_of_truth_reach_the_caller_once_and_not_cache_it()
    {
        await using var host = new CacheHost();
        var calls = 0;

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            host.Cache.GetOrCreateAsync<int>(
                Entry,
                "abc",
                _ => throw new InvalidOperationException($"call {++calls}"),
                Cancel
            )
        );
        var recovered = await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(42), Cancel);

        // Not retried behind the caller's back: a database error is not a cache error.
        failure.Message.ShouldBe("call 1");
        calls.ShouldBe(1);
        recovered.ShouldBe(42);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("../x")]
    public async Task Should_refuse_an_identifier_that_could_collide_with_another_entitys_key(string id)
    {
        await using var host = new CacheHost();

        await Should.ThrowAsync<ArgumentException>(() =>
            host.Cache.GetOrCreateAsync(Entry, id, _ => Task.FromResult(1), Cancel)
        );
    }

    [Fact]
    public async Task Should_count_hits_misses_and_the_duration_with_a_bounded_set_of_dimensions()
    {
        await using var host = new CacheHost();
        using var requests = new MetricCollector<long>(
            host.Get<IMeterFactory>(),
            "Ecommerce.Cache",
            "app.cache.requests"
        );
        using var duration = new MetricCollector<double>(
            host.Get<IMeterFactory>(),
            "Ecommerce.Cache",
            "app.cache.operation.duration"
        );

        await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(1), Cancel);
        await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(1), Cancel);

        var snapshot = requests.GetMeasurementSnapshot();
        snapshot.Select(point => point.Tags["app.cache.outcome"]?.ToString()).ShouldBe(["miss", "hit"]);
        snapshot.ShouldAllBe(point => point.Tags["app.cache.name"]!.ToString() == "test.thing");
        // The key (which holds an id) is never a dimension.
        snapshot.ShouldAllBe(point =>
            point.Tags.Keys.All(key => key.StartsWith("app.cache.", StringComparison.Ordinal))
        );
        duration.GetMeasurementSnapshot().Count.ShouldBe(2);
        duration.GetMeasurementSnapshot().ShouldAllBe(point => point.Value >= 0);
    }

    // ---- Outage ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Should_answer_from_the_source_of_truth_exactly_once_when_valkey_is_unreachable()
    {
        await using var host = new CacheHost(connectionString: Unreachable);
        using var errors = new MetricCollector<long>(host.Get<IMeterFactory>(), "Ecommerce.Cache", "app.cache.errors");
        using var requests = new MetricCollector<long>(
            host.Get<IMeterFactory>(),
            "Ecommerce.Cache",
            "app.cache.requests"
        );
        var calls = 0;

        var started = TimeProvider.System.GetTimestamp();
        var value = await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);
        var elapsed = TimeProvider.System.GetElapsedTime(started);

        value.ShouldBe(1);
        calls.ShouldBe(1);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        errors.GetMeasurementSnapshot().ShouldNotBeEmpty();
        var all = requests.GetMeasurementSnapshot();
        all[^1].Tags["app.cache.outcome"].ShouldBe("error");
    }

    [Fact]
    public async Task Should_swallow_an_eviction_that_cannot_reach_valkey()
    {
        await using var host = new CacheHost(connectionString: Unreachable);

        await host.Cache.RemoveAsync(Entry, "abc", Cancel);
    }

    [Fact]
    public async Task Should_log_an_outage_rarely_and_without_the_connection_string_or_the_key()
    {
        await using var host = new CacheHost(connectionString: Unreachable);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await host.Cache.GetOrCreateAsync(Entry, "secret-looking-id", _ => Task.FromResult(1), Cancel);
        }

        var records = host
            .Logs.Records.Where(record => record.Contains("app.cache.failed", StringComparison.Ordinal))
            .ToArray();
        records.Length.ShouldBe(1);
        records[0].ShouldNotContain("irrelevant");
        records[0].ShouldNotContain("secret-looking-id");
        records[0].ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Should_keep_serving_through_a_valkey_that_dies_and_cache_again_when_it_comes_back()
    {
        await using var server = await ValkeyContainer.StartAsync();
        await using var host = new CacheHost(
            connectionString: server.ConnectionString,
            operationTimeoutMilliseconds: 300
        );
        var calls = 0;
        await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);

        await server.StopServerAsync();
        var during = await host.Cache.GetOrCreateAsync(Entry, "abc", _ => Task.FromResult(++calls), Cancel);
        await server.StartServerAsync();

        during.ShouldBe(2); // The cached value was unreachable, so the source answered.
        var recovered = false;
        for (var attempt = 0; attempt < 40 && !recovered; attempt++)
        {
            await host.Cache.GetOrCreateAsync(Entry, "again", _ => Task.FromResult(++calls), Cancel);
            var keys = await host.KeysAsync();
            recovered = keys.Any(key => key.EndsWith(":again:v1", StringComparison.Ordinal));
            if (!recovered)
            {
                await Task.Delay(500, Cancel);
            }
        }

        recovered.ShouldBeTrue("the client reconnected by itself and cached again");
    }
}
