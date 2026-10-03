using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Domain;

[Trait("Rule", "BR-CUS-002")]
public sealed class ContactEmailTests
{
    [Theory]
    [InlineData("ana.souza@example.com", "ana.souza@example.com")]
    [InlineData("  ANA.Souza@Example.COM ", "ana.souza@example.com")]
    public void Should_trim_and_lowercase_a_valid_address(string raw, string expected)
    {
        var email = ContactEmail.Create(raw);

        email.IsSuccess.ShouldBeTrue();
        email.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    [InlineData("a@@example.com")]
    [InlineData("ana@exämple.com")]
    public void Should_reject_an_address_that_is_not_well_formed(string? raw)
    {
        var error = ContactEmail.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_EMAIL_INVALID");
        error.Field.ShouldBe("email");
        error.RuleId.ShouldBe("BR-CUS-002");
    }

    [Fact]
    public void Should_reject_an_address_longer_than_254_characters()
    {
        var tooLong = new string('a', ContactEmail.MaxLength) + "@example.com";

        ContactEmail.Create(tooLong).ShouldFail().Code.ShouldBe("CUSTOMER_EMAIL_INVALID");
    }

    [Theory]
    [Trait("Rule", "BR-CUS-009")]
    [InlineData("ana.souza@example.com", "a***@example.com")]
    [InlineData("a@example.com", "a***@example.com")]
    [InlineData("very.long.local.part@sub.example.com.br", "v***@sub.example.com.br")]
    public void Should_mask_everything_but_the_first_character_of_the_local_part(string raw, string expected)
    {
        ContactEmail.Create(raw).Value.Masked().ShouldBe(expected);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-009")]
    public void Should_never_reveal_the_address_through_to_string()
    {
        var email = ContactEmail.Create("ana.souza@example.com").Value;

        email.ToString().ShouldNotContain("ana.souza");
    }
}

[Trait("Rule", "BR-CUS-003")]
public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+5511987654321")]
    [InlineData("+12025550123")]
    [InlineData("+12345678")] // 8 digits: the shortest accepted
    [InlineData("+123456789012345")] // 15 digits: the longest accepted
    public void Should_accept_an_e164_number(string raw)
    {
        var phone = PhoneNumber.Create(raw);

        phone.IsSuccess.ShouldBeTrue();
        phone.Value.Value.ShouldBe(raw);
    }

    [Fact]
    public void Should_ignore_surrounding_whitespace_only()
    {
        PhoneNumber.Create("  +5511987654321 ").Value.Value.ShouldBe("+5511987654321");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5511987654321")] // no +
    [InlineData("+0123456789")] // country code cannot start with 0
    [InlineData("+1234567")] // 7 digits
    [InlineData("+1234567890123456")] // 16 digits
    [InlineData("+55 11 98765-4321")] // formatting is the client's job
    [InlineData("+(55)11987654321")]
    [InlineData("+55119876543a1")]
    public void Should_reject_anything_that_is_not_a_plain_e164_number(string? raw)
    {
        var error = PhoneNumber.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_PHONE_INVALID");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe("phone");
        error.RuleId.ShouldBe("BR-CUS-003");
    }

    [Theory]
    [Trait("Rule", "BR-CUS-009")]
    [InlineData("+5511987654321", "+*********4321")]
    [InlineData("+12345678", "+****5678")]
    [InlineData("+123456789012345", "+***********2345")]
    public void Should_mask_every_digit_but_the_last_four_keeping_the_length(string raw, string expected)
    {
        var masked = PhoneNumber.Create(raw).Value.Masked();

        masked.ShouldBe(expected);
        masked.Length.ShouldBe(raw.Length);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-009")]
    public void Should_never_reveal_the_number_through_to_string()
    {
        PhoneNumber.Create("+5511987654321").Value.ToString().ShouldNotContain("1198765");
    }
}
