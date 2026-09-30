using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Domain;

[Trait("Rule", "BR-CAT-001")]
public sealed class ProductRenamingTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_rename_trim_the_name_and_advance_the_version_without_raising_an_event()
    {
        var product = ProductBuilder.New().Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = product.Rename("  Cafeteira Premium  ", _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Name.ShouldBe("Cafeteira Premium");
        product.UpdatedAt.ShouldBe(TestClock.Start.AddMinutes(1));
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        product.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "PRODUCT_NAME_REQUIRED")]
    [InlineData("  ", "PRODUCT_NAME_REQUIRED")]
    [InlineData("ab", "PRODUCT_NAME_LENGTH")]
    public void Should_reject_an_invalid_name_and_leave_the_product_untouched(string? name, string expectedCode)
    {
        var product = ProductBuilder.New().WithName("Cafeteira Elétrica").Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = product.Rename(name, _clock);

        result.ShouldFail().Code.ShouldBe(expectedCode);
        product.Name.ShouldBe("Cafeteira Elétrica");
        product.UpdatedAt.ShouldBe(TestClock.Start);
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }
}

[Trait("Rule", "BR-CAT-001")]
public sealed class ProductDescriptionTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_change_the_description_and_advance_the_version()
    {
        var product = ProductBuilder.New().Build(_clock);

        var result = product.ChangeDescription("  Nova descrição.  ", _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Description.ShouldBe("Nova descrição.");
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        product.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Should_clear_the_description_when_blank()
    {
        var product = ProductBuilder.New().Build(_clock);

        product.ChangeDescription("   ", _clock);

        product.Description.ShouldBeNull();
    }

    [Fact]
    public void Should_reject_a_description_that_is_too_long_and_leave_the_product_untouched()
    {
        var product = ProductBuilder.New().Build(_clock);
        var original = product.Description;

        var result = product.ChangeDescription(new string('d', 2001), _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_DESCRIPTION_TOO_LONG");
        product.Description.ShouldBe(original);
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }
}

[Trait("Rule", "BR-CAT-002")]
public sealed class ProductPriceChangeTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_change_the_price_and_raise_an_event_with_the_old_and_new_price()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = product.ChangePrice(new Money(120m, "BRL"), _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Price.ShouldBe(new Money(120m, "BRL"));
        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductPriceChanged>();
        domainEvent.OldPrice.ShouldBe(new Money(100m, "BRL"));
        domainEvent.NewPrice.ShouldBe(new Money(120m, "BRL"));
    }

    [Fact]
    public void Should_stamp_the_event_with_the_version_and_instant_of_the_change()
    {
        var product = ProductBuilder.New().Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        product.ChangePrice(new Money(1m, "BRL"), _clock);

        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductPriceChanged>();
        domainEvent.AggregateId.ShouldBe(product.Id);
        domainEvent.AggregateVersion.ShouldBe(product.Version);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start.AddMinutes(1));
        domainEvent.EventId.Version.ShouldBe(7);
    }

    [Fact]
    public void Should_do_nothing_when_the_price_is_unchanged()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);

        var result = product.ChangePrice(new Money(100.00m, "BRL"), _clock);

        result.IsSuccess.ShouldBeTrue();
        product.DomainEvents.ShouldBeEmpty();
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
        product.UpdatedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_treat_the_same_amount_in_another_currency_as_a_change()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);

        var result = product.ChangePrice(new Money(100m, "USD"), _clock);

        result.IsSuccess.ShouldBeTrue();
        product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductPriceChanged>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void Should_reject_a_price_that_is_not_greater_than_zero_and_raise_no_event(string amount)
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);
        var price = new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "BRL");

        var result = product.ChangePrice(price, _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
        product.Price.ShouldBe(new Money(100m, "BRL"));
        product.DomainEvents.ShouldBeEmpty();
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }
}
