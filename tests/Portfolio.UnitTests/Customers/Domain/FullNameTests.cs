using System.Text;
using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Domain;

[Trait("Rule", "BR-CUS-001")]
public sealed class FullNameTests
{
    [Theory]
    [InlineData("Ana Souza", "Ana Souza")]
    [InlineData("  Ana   Souza  ", "Ana Souza")]
    [InlineData("José da Silva", "José da Silva")]
    [InlineData("Li", "Li")]
    [InlineData("李小龍", "李小龍")]
    [InlineData("Ana Souza", "Ana Souza")]
    public void Should_trim_collapse_whitespace_and_accept_names_of_any_script(string raw, string expected)
    {
        var name = FullName.Create(raw);

        name.IsSuccess.ShouldBeTrue();
        name.Value.Value.ShouldBe(expected);
    }

    [Fact]
    public void Should_normalize_to_unicode_nfc_so_equal_names_compare_equal()
    {
        // The production image is chiseled, so its runtime has no ICU data and runs in globalization-invariant mode,
        // where Normalize returns its input unchanged. The rule holds wherever ICU is present (development, CI, an
        // image of the -extra flavor); docs/business-rules/customers.md records the gap for the chiseled image.
        Assert.SkipWhen(
            !string.Equals("é".Normalize(NormalizationForm.FormC), "é", StringComparison.Ordinal),
            "This runtime has no ICU data: string normalization is a no-op (globalization-invariant mode)."
        );

        var composed = FullName.Create("José").Value;
        var decomposed = FullName.Create("José").Value;

        decomposed.Value.ShouldBe("José");
        decomposed.ShouldBe(composed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_treat_a_blank_name_as_required(string? raw)
    {
        var error = FullName.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_FULL_NAME_REQUIRED");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe("fullName");
        error.RuleId.ShouldBe("BR-CUS-001");
    }

    [Theory]
    [InlineData("A")]
    [InlineData(" A ")]
    public void Should_reject_a_name_shorter_than_two_characters(string raw)
    {
        var error = FullName.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_FULL_NAME_LENGTH");
        error.ShouldHaveParameters().ShouldContainKeyAndValue("min", FullName.MinLength);
        error.ShouldHaveParameters().ShouldContainKeyAndValue("max", FullName.MaxLength);
    }

    [Fact]
    public void Should_accept_exactly_the_maximum_length_and_reject_one_more()
    {
        FullName.Create(new string('a', FullName.MaxLength)).IsSuccess.ShouldBeTrue();

        FullName
            .Create(new string('a', FullName.MaxLength + 1))
            .ShouldFail()
            .Code.ShouldBe("CUSTOMER_FULL_NAME_LENGTH");
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("-- ..")]
    [InlineData("Ana\tSouza")]
    [InlineData("Ana\nSouza")]
    [InlineData("Ana\u0000Souza")]
    public void Should_reject_control_characters_and_names_without_any_letter(string raw)
    {
        FullName.Create(raw).ShouldFail().Code.ShouldBe("CUSTOMER_FULL_NAME_INVALID");
    }
}
