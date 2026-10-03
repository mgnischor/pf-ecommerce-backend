using Portfolio.Customers.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Domain;

[Trait("Rule", "BR-CUS-004")]
public sealed class CustomerLocaleTests
{
    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData(" en ", "en")]
    [InlineData("EN", "en")]
    public void Should_accept_a_supported_locale_in_any_casing_and_store_the_canonical_one(string raw, string expected)
    {
        var locale = CustomerLocale.Create(raw);

        locale.IsSuccess.ShouldBeTrue();
        locale.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("pt")]
    [InlineData("en-US")]
    [InlineData("xx")]
    public void Should_reject_a_locale_outside_the_supported_set_and_say_which_are_supported(string raw)
    {
        var error = CustomerLocale.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_LOCALE_UNSUPPORTED");
        error.Field.ShouldBe("locale");
        error.ShouldHaveParameters().ShouldContainKeyAndValue("supported", "pt-BR, en");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_treat_a_blank_locale_as_required(string? raw)
    {
        CustomerLocale.Create(raw).ShouldFail().Code.ShouldBe("CUSTOMER_LOCALE_REQUIRED");
    }

    [Fact]
    public void Should_default_to_brazilian_portuguese()
    {
        CustomerLocale.Default.Value.ShouldBe("pt-BR");
    }
}

[Trait("Rule", "BR-CUS-005")]
public sealed class CustomerTimeZoneTests
{
    [Theory]
    [InlineData("America/Sao_Paulo")]
    [InlineData("Europe/Lisbon")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Etc/GMT+3")]
    [InlineData("UTC")]
    public void Should_accept_an_identifier_shaped_like_an_iana_zone(string raw)
    {
        var zone = CustomerTimeZone.Create(raw);

        zone.IsSuccess.ShouldBeTrue();
        zone.Value.Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("Sao_Paulo")] // no area
    [InlineData("Mars/Olympus_Mons")] // not an IANA area
    [InlineData("America/")]
    [InlineData("/Sao_Paulo")]
    [InlineData("America/../etc")]
    [InlineData("America/Sao Paulo")]
    [InlineData("America/Sao_Paulo/Extra/Deep")]
    [InlineData("UTC+3")]
    [InlineData("utc")] // IANA names are case-sensitive
    [InlineData("america/sao_paulo")]
    public void Should_reject_an_identifier_that_is_not_shaped_like_an_iana_zone(string raw)
    {
        var error = CustomerTimeZone.Create(raw).ShouldFail();

        error.Code.ShouldBe("CUSTOMER_TIME_ZONE_INVALID");
        error.Field.ShouldBe("timeZone");
        error.RuleId.ShouldBe("BR-CUS-005");
    }

    [Fact]
    public void Should_reject_an_identifier_longer_than_64_characters()
    {
        var tooLong = "America/" + new string('a', CustomerTimeZone.MaxLength);

        CustomerTimeZone.Create(tooLong).ShouldFail().Code.ShouldBe("CUSTOMER_TIME_ZONE_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Should_treat_a_blank_zone_as_required(string? raw)
    {
        CustomerTimeZone.Create(raw).ShouldFail().Code.ShouldBe("CUSTOMER_TIME_ZONE_REQUIRED");
    }

    [Fact]
    public void Should_default_to_sao_paulo()
    {
        CustomerTimeZone.Default.Value.ShouldBe("America/Sao_Paulo");
    }
}
