using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Identity.Support;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Application;

[Trait("Rule", "BR-IDN-005")]
public sealed class RefreshTokenHandlerTests
{
    private readonly IdentityWorld _world = new();

    private async Task<(User User, TokenPair Pair)> SignedInAsync()
    {
        var user = await _world.AddUserAsync();
        var pair = (
            await _world.SignIn.HandleAsync(
                new SignInCommand("ana.souza@example.com", IdentityWorld.StrongPassword),
                TestContext.Current.CancellationToken
            )
        ).Value;
        _world.Audit.Events.Clear();
        return (user, pair);
    }

    private Task<Result<TokenPair>> RefreshAsync(string? token) =>
        _world.Refresh.HandleAsync(new RefreshTokenCommand(token), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_consume_the_token_and_issue_its_successor_in_the_same_family()
    {
        var (user, first) = await SignedInAsync();

        var second = (await RefreshAsync(first.RefreshToken)).Value;

        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        var tokens = await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken);
        tokens.Count.ShouldBe(2);
        tokens.Select(t => t.FamilyId).Distinct().Count().ShouldBe(1);
        tokens.Count(t => t.UsedAt is not null).ShouldBe(1);
    }

    [Fact]
    public async Task Should_never_extend_a_session_beyond_its_absolute_expiry()
    {
        var (user, pair) = await SignedInAsync();

        // Rotate every six days, always inside the seven-day token lifetime.
        pair = await RefreshAfterAsync(pair, days: 6);
        pair = await RefreshAfterAsync(pair, days: 6);
        pair = await RefreshAfterAsync(pair, days: 6);
        _ = await RefreshAfterAsync(pair, days: 6);

        var newest = (await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken))
            .OrderBy(token => token.CreatedAt)
            .Last();
        newest.ExpiresAt.ShouldBe(newest.FamilyExpiresAt);
        newest.ExpiresAt.ShouldBe(TestClock.Start.AddDays(30));
    }

    [Fact]
    public async Task Should_refuse_to_rotate_once_the_session_reached_its_absolute_expiry()
    {
        var (_, pair) = await SignedInAsync();
        pair = await RefreshAfterAsync(pair, days: 6);
        pair = await RefreshAfterAsync(pair, days: 6);
        pair = await RefreshAfterAsync(pair, days: 6);
        pair = await RefreshAfterAsync(pair, days: 6);

        _world.Clock.Advance(TimeSpan.FromDays(6));
        var result = await RefreshAsync(pair.RefreshToken);

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Should_answer_a_replayed_token_with_the_generic_error_revoke_the_session_and_invalidate_access_tokens()
    {
        var (user, first) = await SignedInAsync();
        var second = (await RefreshAsync(first.RefreshToken)).Value;

        var replay = await RefreshAsync(first.RefreshToken);
        var successor = await RefreshAsync(second.RefreshToken);

        replay.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        successor.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        (await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken)).ShouldAllBe(t =>
            t.RevokedAt != null
        );
        user.TokenVersion.ShouldBe(2);
        _world.Audit.Events.ShouldContain("refresh_token_reuse");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-issued")]
    public async Task Should_answer_an_unknown_token_with_the_generic_error_and_no_audit_of_reuse(string? token)
    {
        await SignedInAsync();

        var result = await RefreshAsync(token);

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        _world.Audit.Events.ShouldNotContain("refresh_token_reuse");
    }

    [Fact]
    public async Task Should_refuse_a_token_longer_than_any_issued_one_without_looking_it_up()
    {
        await SignedInAsync();

        var result = await RefreshAsync(new string('x', 513));

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Should_reject_an_expired_token()
    {
        var (_, first) = await SignedInAsync();
        _world.Clock.Advance(TimeSpan.FromDays(7));

        var result = await RefreshAsync(first.RefreshToken);

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        _world.Audit.Events.ShouldNotContain("refresh_token_reuse");
    }

    [Fact]
    public async Task Should_revoke_the_session_when_the_account_was_deactivated()
    {
        var (user, first) = await SignedInAsync();
        user.Deactivate(_world.Clock);

        var result = await RefreshAsync(first.RefreshToken);

        result.ShouldFail().Code.ShouldBe("INVALID_REFRESH_TOKEN");
        (await _world.RefreshTokens.ListByUserAsync(user.Id, TestContext.Current.CancellationToken)).ShouldAllBe(t =>
            t.RevokedAt != null
        );
    }

    [Fact]
    public async Task Should_issue_the_new_access_token_with_the_current_level_after_a_level_change()
    {
        var (user, first) = await SignedInAsync();
        user.ChangeAccessLevel(AccessLevel.Manager, _world.Clock);

        var second = (await RefreshAsync(first.RefreshToken)).Value;

        second.AccessToken.ShouldEndWith("Manager:2");
    }

    private async Task<TokenPair> RefreshAfterAsync(TokenPair pair, int days)
    {
        _world.Clock.Advance(TimeSpan.FromDays(days));
        return (await RefreshAsync(pair.RefreshToken)).Value;
    }
}
