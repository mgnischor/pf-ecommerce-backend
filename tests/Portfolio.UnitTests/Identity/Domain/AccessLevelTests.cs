using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Identity.Domain;

[Trait("Rule", "BR-IDN-004")]
public sealed class AccessLevelTests
{
    [Theory]
    [InlineData("public", 0)]
    [InlineData("collaborator", 1)]
    [InlineData("manager", 2)]
    [InlineData("administrator", 3)]
    [InlineData("developer", 4)]
    public void Should_round_trip_every_level_through_its_wire_name(string wireName, int rank)
    {
        AccessLevels.TryParseWireName(wireName, out var level).ShouldBeTrue();

        ((int)level).ShouldBe(rank);
        level.ToWireName().ShouldBe(wireName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Manager")]
    [InlineData("MANAGER")]
    [InlineData(" manager")]
    [InlineData("manager ")]
    [InlineData("superuser")]
    [InlineData("root")]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("5")]
    public void Should_refuse_anything_that_is_not_an_exact_known_wire_name(string? raw)
    {
        AccessLevels.TryParseWireName(raw, out var level).ShouldBeFalse();

        level.ShouldBe(AccessLevel.Public);
    }

    [Fact]
    public void Should_refuse_to_name_an_undefined_level()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((AccessLevel)99).ToWireName());
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(3, 2, true)]
    [InlineData(3, 4, false)]
    [InlineData(4, 3, true)]
    [InlineData(4, 4, true)]
    public void Should_be_hierarchical_so_a_higher_level_holds_every_lower_one(int held, int required, bool expected)
    {
        ((AccessLevel)held).Satisfies((AccessLevel)required).ShouldBe(expected);
    }
}
