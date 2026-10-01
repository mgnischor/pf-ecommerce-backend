using Portfolio.Identity.Domain;

namespace Portfolio.UnitTests.Identity.Domain;

[Trait("Rule", "BR-IDN-001")]
public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("ana.souza@example.com", "ana.souza@example.com")]
    [InlineData("  Ana.Souza@Example.COM  ", "ana.souza@example.com")]
    [InlineData("a+tag@sub.example.com.br", "a+tag@sub.example.com.br")]
    [InlineData("a_b-c@exa-mple.io", "a_b-c@exa-mple.io")]
    public void Should_normalize_to_trimmed_lowercase(string raw, string expected)
    {
        EmailAddress.Create(raw).Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("two@@example.com")]
    [InlineData("a@b@example.com")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@example")]
    [InlineData("ana@.example.com")]
    [InlineData("ana@example..com")]
    [InlineData("ana@-example.com")]
    [InlineData("ana@example.c")]
    [InlineData(".ana@example.com")]
    [InlineData("ana.@example.com")]
    [InlineData("an..a@example.com")]
    [InlineData("ana souza@example.com")]
    [InlineData("ana\t@example.com")]
    [InlineData("\"ana\"@example.com")]
    [InlineData("<ana>@example.com")]
    [InlineData("ana,b@example.com")]
    [InlineData("ána@example.com")]
    [InlineData("ana@exämple.com")]
    public void Should_reject_an_invalid_address(string? raw)
    {
        var result = EmailAddress.Create(raw);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("EMAIL_INVALID");
        result.Error.RuleId.ShouldBe("BR-IDN-001");
    }

    [Fact]
    public void Should_reject_a_local_part_longer_than_sixty_four_characters()
    {
        EmailAddress.Create(new string('a', 65) + "@example.com").IsFailure.ShouldBeTrue();
        EmailAddress.Create(new string('a', 64) + "@example.com").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_reject_an_address_longer_than_two_hundred_fifty_four_characters()
    {
        var tooLong = "a@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 5)) + ".com";

        EmailAddress.Create(tooLong).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Should_treat_addresses_that_differ_only_by_case_as_equal()
    {
        EmailAddress.Create("Ana@Example.com").Value.ShouldBe(EmailAddress.Create("ana@example.com").Value);
    }

    [Fact]
    public void Should_expose_the_local_part()
    {
        EmailAddress.Create("mariana.souza@example.com").Value.LocalPart.ShouldBe("mariana.souza");
    }

    [Fact]
    public void Should_render_as_its_normalized_value()
    {
        EmailAddress.Create("Ana@Example.com").Value.ToString().ShouldBe("ana@example.com");
    }
}
