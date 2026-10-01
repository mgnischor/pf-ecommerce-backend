using Portfolio.Identity.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Domain;

[Trait("Rule", "BR-IDN-005")]
public sealed class RefreshTokenTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private RefreshToken NewToken(TimeSpan? lifetime = null, TimeSpan? familyLifetime = null) =>
        RefreshToken.Issue(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "hash-1",
            _clock.GetUtcNow() + (lifetime ?? TimeSpan.FromDays(7)),
            _clock.GetUtcNow() + (familyLifetime ?? TimeSpan.FromDays(30)),
            _clock
        );

    [Fact]
    public void Should_consume_an_unused_token_once()
    {
        var token = NewToken();

        var result = token.Use(_clock);

        result.IsSuccess.ShouldBeTrue();
        token.UsedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_flag_a_second_use_as_reuse_so_the_session_can_be_revoked()
    {
        var token = NewToken();
        token.Use(_clock);

        var result = token.Use(_clock);

        result.ShouldFail().Code.ShouldBe("REFRESH_TOKEN_REUSED");
    }

    [Fact]
    public void Should_let_exactly_one_of_many_concurrent_uses_win()
    {
        var token = NewToken();

        var outcomes = Enumerable
            .Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(16)
            .Select(_ => token.Use(_clock).IsSuccess)
            .ToArray();

        outcomes.Count(won => won).ShouldBe(1);
    }

    [Fact]
    public void Should_reject_an_expired_token_as_invalid_not_as_reuse()
    {
        var token = NewToken(lifetime: TimeSpan.FromDays(7));
        _clock.Advance(TimeSpan.FromDays(7));

        var result = token.Use(_clock);

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        token.UsedAt.ShouldBeNull();
    }

    [Fact]
    public void Should_reject_a_token_whose_family_reached_its_absolute_expiry()
    {
        var token = NewToken(lifetime: TimeSpan.FromDays(7), familyLifetime: TimeSpan.FromDays(7));
        _clock.Advance(TimeSpan.FromDays(7));

        token.Use(_clock).ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public void Should_reject_a_revoked_token()
    {
        var token = NewToken();
        token.Revoke(_clock);

        token.Use(_clock).ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public void Should_revoke_idempotently()
    {
        var token = NewToken();
        token.Revoke(_clock);
        var revokedAt = token.RevokedAt;
        _clock.Advance(TimeSpan.FromHours(1));

        token.Revoke(_clock);

        token.RevokedAt.ShouldBe(revokedAt);
    }

    [Fact]
    public void Should_refuse_a_token_that_outlives_its_family()
    {
        Should.Throw<ArgumentException>(() =>
            RefreshToken.Issue(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "h",
                _clock.GetUtcNow().AddDays(31),
                _clock.GetUtcNow().AddDays(30),
                _clock
            )
        );
    }

    [Fact]
    public void Should_refuse_empty_identifiers()
    {
        Should.Throw<ArgumentException>(() =>
            RefreshToken.Issue(
                Guid.Empty,
                Guid.CreateVersion7(),
                "h",
                _clock.GetUtcNow().AddDays(1),
                _clock.GetUtcNow().AddDays(2),
                _clock
            )
        );
    }

    [Fact]
    public void Should_refuse_a_missing_hash()
    {
        Should.Throw<ArgumentException>(() =>
            RefreshToken.Issue(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                " ",
                _clock.GetUtcNow().AddDays(1),
                _clock.GetUtcNow().AddDays(2),
                _clock
            )
        );
    }
}
