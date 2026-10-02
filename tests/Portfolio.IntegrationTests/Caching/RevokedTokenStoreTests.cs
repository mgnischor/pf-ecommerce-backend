using Portfolio.Identity.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.IntegrationTests.Caching;

/// <summary>
/// The <c>jti</c> blocklist in Valkey (ai/SECURITY.md §7.6): bounded by the token lifetime, shared by every instance, and
/// fail-closed by default when it cannot be read.
/// </summary>
public sealed class RevokedTokenStoreTests
{
    private const string Unreachable = "127.0.0.1:1,password=irrelevant";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static DateTimeOffset In(TimeSpan span) => TimeProvider.System.GetUtcNow() + span;

    [Fact]
    public async Task Should_block_a_token_until_it_would_have_expired_anyway()
    {
        await using var host = new CacheHost();
        var store = host.Get<IRevokedTokenStore>();

        await store.RevokeAsync("jti-1", In(TimeSpan.FromMinutes(10)), Cancel);

        (await store.IsRevokedAsync("jti-1", Cancel)).ShouldBeTrue();
        (await store.IsRevokedAsync("jti-2", Cancel)).ShouldBeFalse();
        var key = (await host.KeysAsync()).ShouldHaveSingleItem();
        key.ShouldBe($"{host.Prefix}:identity:revoked-jti:jti-1:v1");
        (await host.InspectAsync(key)).Ttl.ShouldNotBeNull().TotalSeconds.ShouldBeInRange(500, 600);
    }

    [Fact]
    public async Task Should_forget_a_block_when_the_token_expires_so_the_set_stays_bounded()
    {
        await using var host = new CacheHost();
        var store = host.Get<IRevokedTokenStore>();
        await store.RevokeAsync("jti-short", In(TimeSpan.FromSeconds(1)), Cancel);

        await Task.Delay(TimeSpan.FromMilliseconds(1_700), Cancel);

        (await store.IsRevokedAsync("jti-short", Cancel)).ShouldBeFalse();
        (await host.KeysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_not_store_a_block_for_a_token_that_already_expired()
    {
        await using var host = new CacheHost();

        await host.Get<IRevokedTokenStore>().RevokeAsync("jti-old", In(TimeSpan.FromMinutes(-1)), Cancel);

        (await host.KeysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_share_the_block_between_instances()
    {
        await using var first = new CacheHost();
        await using var second = new CacheHost(prefix: first.Prefix);
        await first.Get<IRevokedTokenStore>().RevokeAsync("jti-shared", In(TimeSpan.FromMinutes(5)), Cancel);

        // The old in-process store could not do this: a sign-out on one replica did not reach the others.
        (await second.Get<IRevokedTokenStore>().IsRevokedAsync("jti-shared", Cancel)).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_reject_the_token_when_the_blocklist_cannot_be_read_by_default()
    {
        await using var host = new CacheHost(connectionString: Unreachable);

        var revoked = await host.Get<IRevokedTokenStore>().IsRevokedAsync("jti-1", Cancel);

        revoked.ShouldBeTrue("authentication fails closed");
        var record = host.Logs.Records.Single(entry =>
            entry.Contains("identity.revocation_check.failed", StringComparison.Ordinal)
        );
        record.ShouldContain("rejected");
        record.ShouldNotContain("jti-1");
        record.ShouldNotContain("irrelevant");
    }

    [Fact]
    public async Task Should_accept_the_token_when_the_operator_chose_availability_over_a_strict_blocklist()
    {
        await using var host = new CacheHost(connectionString: Unreachable, failureMode: RevocationFailureMode.Allow);

        var revoked = await host.Get<IRevokedTokenStore>().IsRevokedAsync("jti-1", Cancel);

        revoked.ShouldBeFalse();
        host.Logs.Records.ShouldContain(entry => entry.Contains("accepted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_not_pretend_a_sign_out_revoked_a_token_when_the_write_failed()
    {
        await using var host = new CacheHost(connectionString: Unreachable);

        await Should.ThrowAsync<Exception>(() =>
            host.Get<IRevokedTokenStore>().RevokeAsync("jti-1", In(TimeSpan.FromMinutes(5)), Cancel)
        );
    }
}
