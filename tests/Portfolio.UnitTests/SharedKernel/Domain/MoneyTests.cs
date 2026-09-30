using System.Globalization;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.SharedKernel.Domain;

public sealed class MoneyCreationTests
{
    [Fact]
    public void Should_normalize_currency_to_uppercase_when_created()
    {
        var money = new Money(10m, "brl");

        money.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData("1.00005", "1.0000")]
    [InlineData("1.00015", "1.0002")]
    [InlineData("2.00025", "2.0002")]
    [InlineData("10.12345", "10.1234")]
    [InlineData("10.12346", "10.1235")]
    public void Should_round_half_to_even_at_four_decimal_places_when_created(string amount, string expected)
    {
        var money = new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), "BRL");

        money.Amount.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("BR")]
    [InlineData("BRLL")]
    [InlineData("B2N")]
    [InlineData("R$L")]
    [InlineData("ÇÃO")]
    public void Should_reject_currency_that_is_not_three_ascii_letters(string currency)
    {
        Should.Throw<ArgumentException>(() => new Money(1m, currency));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("B2N")]
    public void Should_report_invalid_currency_as_failure_when_created_from_untrusted_input(string? currency)
    {
        var result = Money.Create(10m, currency);

        result.IsFailure.ShouldBeTrue();
        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        result.ShouldFail().Field.ShouldBe("currency");
    }

    [Fact]
    public void Should_create_money_when_untrusted_input_is_valid()
    {
        var result = Money.Create(10.5m, "usd");

        result.Value.ShouldBe(new Money(10.5m, "USD"));
    }

    [Fact]
    public void Should_create_zero_amount_in_the_given_currency()
    {
        var zero = Money.Zero("BRL");

        zero.Amount.ShouldBe(0m);
        zero.IsPositive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("0.0001", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("-0.0001", false)]
    [InlineData("0.00004", false)]
    public void Should_be_positive_only_when_the_rounded_amount_is_greater_than_zero(string amount, bool expected)
    {
        var money = new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), "BRL");

        money.IsPositive.ShouldBe(expected);
    }
}

public sealed class MoneyCalculationTests
{
    [Fact]
    public void Should_add_amounts_in_the_same_currency()
    {
        var total = new Money(10.10m, "BRL").Add(new Money(0.20m, "BRL"));

        total.ShouldBe(new Money(10.30m, "BRL"));
    }

    [Fact]
    public void Should_subtract_amounts_in_the_same_currency()
    {
        var difference = new Money(10.00m, "BRL").Subtract(new Money(12.50m, "BRL"));

        difference.ShouldBe(new Money(-2.50m, "BRL"));
    }

    [Fact]
    public void Should_reject_addition_when_currencies_differ()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new Money(1m, "BRL").Add(new Money(1m, "USD")));

        exception.Message.ShouldContain("BRL");
        exception.Message.ShouldContain("USD");
    }

    [Fact]
    public void Should_reject_subtraction_when_currencies_differ()
    {
        Should.Throw<InvalidOperationException>(() => new Money(1m, "BRL").Subtract(new Money(1m, "EUR")));
    }

    [Theory]
    [InlineData("19.99", 3, "59.97")]
    [InlineData("0.33", 3, "0.99")]
    [InlineData("100", 0, "0")]
    [InlineData("100", 1, "100")]
    public void Should_multiply_unit_price_by_quantity(string unitPrice, int quantity, string expected)
    {
        var total = new Money(decimal.Parse(unitPrice, CultureInfo.InvariantCulture), "BRL").Multiply(quantity);

        total.Amount.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Should_reject_negative_quantity_when_multiplying()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Money(1m, "BRL").Multiply(-1));
    }

    [Fact]
    public void Should_not_accumulate_binary_floating_point_error_when_adding_many_small_amounts()
    {
        var tenCents = Enumerable.Repeat(new Money(0.1m, "BRL"), 10);

        var total = tenCents.Aggregate(Money.Zero("BRL"), (sum, next) => sum.Add(next));

        total.Amount.ShouldBe(1.0m);
    }
}

public sealed class MoneyEqualityTests
{
    [Fact]
    public void Should_be_equal_when_amount_and_currency_match_regardless_of_trailing_zeros()
    {
        new Money(10.0m, "brl").ShouldBe(new Money(10.00m, "BRL"));
    }

    [Fact]
    public void Should_have_the_same_hash_code_when_equal()
    {
        new Money(10.0m, "BRL").GetHashCode().ShouldBe(new Money(10.00m, "BRL").GetHashCode());
    }

    [Fact]
    public void Should_not_be_equal_when_currency_differs()
    {
        new Money(10m, "BRL").ShouldNotBe(new Money(10m, "USD"));
    }

    [Fact]
    public void Should_format_with_the_invariant_culture_even_when_the_current_culture_uses_a_decimal_comma()
    {
        using var culture = new CultureScope("pt-BR");

        new Money(1234.5m, "BRL").ToString().ShouldBe("1234.50 BRL");
    }
}
