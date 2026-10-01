using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Inventory.Domain;

[Trait("Rule", "BR-INV-007")]
public sealed class SkuTests
{
    [Theory]
    [InlineData("CAF-600-PRT", "CAF-600-PRT")]
    [InlineData("  caf-600_prt  ", "CAF-600_PRT")]
    [InlineData("ab12", "AB12")]
    public void Should_trim_and_normalize_a_valid_sku_to_uppercase(string raw, string expected)
    {
        var sku = Sku.Create(raw);

        sku.IsSuccess.ShouldBeTrue();
        sku.Value.Value.ShouldBe(expected);
    }

    [Fact]
    public void Should_treat_skus_that_differ_only_by_case_or_padding_as_equal()
    {
        Sku.Create("caf-600").Value.ShouldBe(Sku.Create(" CAF-600 ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_reject_a_missing_sku(string? raw)
    {
        var error = Sku.Create(raw).ShouldFail();

        error.Code.ShouldBe("INVENTORY_SKU_REQUIRED");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe("sku");
        error.RuleId.ShouldBe("BR-INV-007");
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("A1234567890123456789012345678901234")]
    public void Should_reject_a_sku_outside_the_allowed_length(string raw)
    {
        var error = Sku.Create(raw).ShouldFail();

        error.Code.ShouldBe("INVENTORY_SKU_LENGTH");
        error.ShouldHaveParameters()["min"].ShouldBe(Sku.MinLength);
        error.ShouldHaveParameters()["max"].ShouldBe(Sku.MaxLength);
    }

    [Theory]
    [InlineData("CAF 600")]
    [InlineData("CAF/600")]
    [InlineData("CAFÉ-600")]
    [InlineData("ＣＡＦ-600")]
    public void Should_reject_characters_other_than_ascii_letters_digits_hyphen_and_underscore(string raw)
    {
        Sku.Create(raw).ShouldFail().Code.ShouldBe("INVENTORY_SKU_INVALID_CHARACTERS");
    }
}
