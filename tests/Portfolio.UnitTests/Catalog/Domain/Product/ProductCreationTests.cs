using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Domain;

public sealed class ProductCreationTests
{
    private static readonly Sku AnySku = Sku.Create("CAF-600-PRT").Value;
    private static readonly Money AnyPrice = new(189.90m, "BRL");
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_create_a_draft_product_with_normalized_data_and_the_clock_timestamps()
    {
        var result = Product.Create("  Cafeteira Elétrica  ", AnySku, AnyPrice, "  Com filtro permanente.  ", _clock);

        var product = result.Value;
        product.Name.ShouldBe("Cafeteira Elétrica");
        product.Description.ShouldBe("Com filtro permanente.");
        product.Sku.ShouldBe(AnySku);
        product.Price.ShouldBe(AnyPrice);
        product.Status.ShouldBe(ProductStatus.Draft);
        product.CreatedAt.ShouldBe(TestClock.Start);
        product.UpdatedAt.ShouldBe(TestClock.Start);
        product.DeletedAt.ShouldBeNull();
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
        product.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Should_raise_a_single_product_created_event_carrying_the_initial_snapshot()
    {
        var product = Product.Create("Cafeteira Elétrica", AnySku, AnyPrice, null, _clock).Value;

        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductCreated>();
        domainEvent.AggregateId.ShouldBe(product.Id);
        domainEvent.AggregateVersion.ShouldBe(product.Version);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start);
        domainEvent.EventId.Version.ShouldBe(7);
        domainEvent.Name.ShouldBe("Cafeteira Elétrica");
        domainEvent.Sku.ShouldBe("CAF-600-PRT");
        domainEvent.Price.ShouldBe(AnyPrice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_reject_a_missing_name(string? name)
    {
        var result = Product.Create(name, AnySku, AnyPrice, null, _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_REQUIRED");
        result.ShouldFail().RuleId.ShouldBe("BR-CAT-001");
        result.ShouldFail().Field.ShouldBe("name");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  ab  ")]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_reject_a_name_shorter_than_three_characters_after_trimming(string name)
    {
        var result = Product.Create(name, AnySku, AnyPrice, null, _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_LENGTH");
    }

    [Fact]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_reject_a_name_longer_than_two_hundred_characters()
    {
        var result = Product.Create(new string('a', 201), AnySku, AnyPrice, null, _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_LENGTH");
        result.ShouldFail().ShouldHaveParameters()["min"].ShouldBe(3);
        result.ShouldFail().ShouldHaveParameters()["max"].ShouldBe(200);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(200)]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_accept_the_boundary_name_lengths(int length)
    {
        var result = Product.Create(new string('a', length), AnySku, AnyPrice, null, _clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_reject_a_description_longer_than_two_thousand_characters()
    {
        var result = Product.Create("Cafeteira", AnySku, AnyPrice, new string('d', 2001), _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_DESCRIPTION_TOO_LONG");
        result.ShouldFail().Field.ShouldBe("description");
    }

    [Fact]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_accept_a_description_of_exactly_two_thousand_characters()
    {
        var result = Product.Create("Cafeteira", AnySku, AnyPrice, new string('d', 2000), _clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Rule", "BR-CAT-001")]
    public void Should_store_a_blank_description_as_null(string? description)
    {
        var product = Product.Create("Cafeteira", AnySku, AnyPrice, description, _clock).Value;

        product.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("0.00004")]
    [Trait("Rule", "BR-CAT-002")]
    public void Should_reject_a_price_that_is_not_greater_than_zero(string amount)
    {
        var price = new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "BRL");

        var result = Product.Create("Cafeteira", AnySku, price, null, _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
        result.ShouldFail().RuleId.ShouldBe("BR-CAT-002");
    }

    [Fact]
    public void Should_report_the_name_error_first_when_several_rules_are_violated()
    {
        var result = Product.Create("", AnySku, new Money(0m, "BRL"), new string('d', 2001), _clock);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_REQUIRED");
    }

    [Fact]
    public void Should_reject_a_missing_clock()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Product.Create("Cafeteira", AnySku, AnyPrice, null, null!));
    }

    [Fact]
    public void Should_reject_a_missing_sku()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Product.Create("Cafeteira", null!, AnyPrice, null, _clock));
    }

    [Fact]
    public void Should_reject_a_missing_price()
    {
        // Justification for the suppression: the test passes null on purpose to verify the guard clause.
        Should.Throw<ArgumentNullException>(() => Product.Create("Cafeteira", AnySku, null!, null, _clock));
    }
}
