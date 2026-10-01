using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Identity.Application;

[Trait("Rule", "BR-IDN-004")]
public sealed class AccessManagementRulesTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private User UserAt(AccessLevel level) =>
        User.Register(EmailAddress.Create($"{level}@example.com".ToLowerInvariant()).Value, "hash", level, _clock);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void Should_refuse_granting_when_the_caller_is_below_administrator(int actor, int target)
    {
        var result = AccessManagementRules.CanGrant((AccessLevel)actor, (AccessLevel)target);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INSUFFICIENT");
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    [InlineData(4, 4)]
    public void Should_allow_granting_up_to_the_callers_own_level(int actor, int target)
    {
        AccessManagementRules.CanGrant((AccessLevel)actor, (AccessLevel)target).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_refuse_an_administrator_granting_developer()
    {
        var result = AccessManagementRules.CanGrant(AccessLevel.Administrator, AccessLevel.Developer);

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_ESCALATION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Should_refuse_managing_accounts_below_administrator(int actor)
    {
        var result = AccessManagementRules.CanManage(
            Guid.CreateVersion7(),
            (AccessLevel)actor,
            UserAt(AccessLevel.Public)
        );

        result.ShouldFail().Code.ShouldBe("ACCESS_LEVEL_INSUFFICIENT");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Should_let_an_administrator_manage_accounts_strictly_below(int targetLevel)
    {
        var result = AccessManagementRules.CanManage(
            Guid.CreateVersion7(),
            AccessLevel.Administrator,
            UserAt((AccessLevel)targetLevel)
        );

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Should_refuse_an_administrator_managing_a_peer_or_a_superior(int targetLevel)
    {
        var result = AccessManagementRules.CanManage(
            Guid.CreateVersion7(),
            AccessLevel.Administrator,
            UserAt((AccessLevel)targetLevel)
        );

        result.ShouldFail().Code.ShouldBe("USER_NOT_MANAGEABLE");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public void Should_let_a_developer_manage_any_other_account(int targetLevel)
    {
        var result = AccessManagementRules.CanManage(
            Guid.CreateVersion7(),
            AccessLevel.Developer,
            UserAt((AccessLevel)targetLevel)
        );

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Should_never_let_anyone_manage_themselves(int level)
    {
        var self = UserAt((AccessLevel)level);

        var result = AccessManagementRules.CanManage(self.Id, (AccessLevel)level, self);

        result.ShouldFail().Code.ShouldBe("USER_NOT_MANAGEABLE");
    }
}
