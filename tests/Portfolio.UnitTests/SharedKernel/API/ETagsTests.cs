using Portfolio.SharedKernel.API;

namespace Portfolio.UnitTests.SharedKernel.API;

public sealed class ETagsTests
{
    [Theory]
    [InlineData(1, "\"1\"")]
    [InlineData(42, "\"42\"")]
    public void Should_format_the_version_as_a_quoted_tag(int version, string expected)
    {
        ETags.ForVersion(version).ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"1\"", 1)]
    [InlineData("W/\"7\"", 7)]
    [InlineData("  \"12\"  ", 12)]
    public void Should_read_the_version_out_of_a_tag_it_issued(string header, int expected)
    {
        ETags.TryParseVersion(header, out var version).ShouldBeTrue();

        version.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("1")]
    [InlineData("\"\"")]
    [InlineData("\"0\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"+1\"")]
    [InlineData("\"abc\"")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("\"99999999999\"")]
    public void Should_reject_anything_that_is_not_one_exact_version_tag(string? header)
    {
        ETags.TryParseVersion(header, out _).ShouldBeFalse();
    }

    [Fact]
    public void Should_round_trip_every_version_it_formats()
    {
        ETags.TryParseVersion(ETags.ForVersion(2026), out var version).ShouldBeTrue();

        version.ShouldBe(2026);
    }
}
