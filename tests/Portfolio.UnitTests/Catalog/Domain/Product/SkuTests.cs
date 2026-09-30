using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Domain;

[Trait("Rule", "BR-CAT-004")]
public sealed class SkuFormatTests
{
    [Theory]
    [InlineData("caf-600-prt", "CAF-600-PRT")]
    [InlineData("  ABC_123  ", "ABC_123")]
    [InlineData("Sku-0001", "SKU-0001")]
    public void Should_normalize_to_trimmed_uppercase(string raw, string expected)
    {
        var result = Sku.Create(raw);

        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(32)]
    public void Should_accept_the_boundary_lengths(int length)
    {
        var result = Sku.Create(new string('A', length));

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_reject_a_missing_sku(string? raw)
    {
        var result = Sku.Create(raw);

        result.ShouldFail().Code.ShouldBe("SKU_REQUIRED");
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        result.ShouldFail().RuleId.ShouldBe("BR-CAT-004");
        result.ShouldFail().Field.ShouldBe("sku");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(33)]
    public void Should_reject_a_length_outside_the_allowed_range(int length)
    {
        var result = Sku.Create(new string('A', length));

        result.ShouldFail().Code.ShouldBe("SKU_LENGTH");
        result.ShouldFail().ShouldHaveParameters()["min"].ShouldBe(4);
        result.ShouldFail().ShouldHaveParameters()["max"].ShouldBe(32);
    }

    [Theory]
    [InlineData("ABC 123")]
    [InlineData("ABC/123")]
    [InlineData("ABC.123")]
    [InlineData("ÇÃO-1234")]
    [InlineData("SKU-１２３４")]
    [InlineData("AB​CD")]
    public void Should_reject_characters_other_than_ascii_letters_digits_hyphen_and_underscore(string raw)
    {
        var result = Sku.Create(raw);

        result.ShouldFail().Code.ShouldBe("SKU_INVALID_CHARACTERS");
    }

    [Fact]
    public void Should_treat_skus_that_differ_only_by_case_and_padding_as_equal()
    {
        var first = Sku.Create(" abc-001 ").Value;
        var second = Sku.Create("ABC-001").Value;

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Should_render_as_its_normalized_code()
    {
        Sku.Create("abc-001").Value.ToString().ShouldBe("ABC-001");
    }
}
