using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Cart.Domain;

[Trait("Rule", "BR-CRT-005")]
public sealed class CatalogProductTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private CatalogProduct NewProduct(int version = 1, decimal price = 100m, string currency = "BRL") =>
        CatalogProduct.Register(
            Guid.CreateVersion7(),
            "CAF-600-PRT",
            "Cafeteira Elétrica",
            new Money(price, currency),
            version,
            _clock
        );

    [Fact]
    public void Should_start_as_a_draft_that_is_not_sellable_and_keep_the_catalog_identifier()
    {
        var id = Guid.CreateVersion7();

        var product = CatalogProduct.Register(id, "CAF-600-PRT", "Cafeteira", new Money(10m, "BRL"), 1, _clock);

        product.Id.ShouldBe(id);
        product.Sellable.ShouldBeFalse();
        (product.PriceVersion, product.StatusVersion).ShouldBe((1, 1));
    }

    [Fact]
    public void Should_follow_the_price_forward()
    {
        var product = NewProduct(price: 100m);

        var applied = product.ApplyPrice(new Money(120m, "BRL"), 2, _clock);

        applied.ShouldBeTrue();
        product.Price.ShouldBe(new Money(120m, "BRL"));
        product.PriceVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Should_ignore_a_price_event_that_is_not_newer_than_the_one_applied(int sourceVersion)
    {
        var product = NewProduct(price: 100m);
        product.ApplyPrice(new Money(120m, "BRL"), 3, _clock);

        var applied = product.ApplyPrice(new Money(999m, "BRL"), sourceVersion, _clock);

        applied.ShouldBeFalse();
        product.Price.ShouldBe(new Money(120m, "BRL"));
    }

    [Fact]
    public void Should_become_sellable_when_the_product_is_activated()
    {
        var product = NewProduct();

        product.ApplyStatus(sellable: true, 2, _clock).ShouldBeTrue();

        product.Sellable.ShouldBeTrue();
    }

    [Fact]
    public void Should_stop_being_sellable_when_the_product_is_deleted()
    {
        var product = NewProduct();
        product.ApplyStatus(sellable: true, 2, _clock);

        product.ApplyDeletion(5, _clock).ShouldBeTrue();

        product.Sellable.ShouldBeFalse();
    }

    [Fact]
    public void Should_not_let_an_older_status_event_undo_a_newer_deletion()
    {
        var product = NewProduct();
        product.ApplyDeletion(5, _clock);

        // The activation (version 2) arrives after the deletion (version 5).
        var applied = product.ApplyStatus(sellable: true, 2, _clock);

        applied.ShouldBeFalse();
        product.Sellable.ShouldBeFalse();
    }

    [Fact]
    public void Should_keep_the_price_and_status_facets_independent_when_events_interleave()
    {
        var product = NewProduct(price: 100m);

        // Status v3 is processed before price v2: the price must still be applied.
        product.ApplyStatus(sellable: true, 3, _clock);
        var priceApplied = product.ApplyPrice(new Money(150m, "BRL"), 2, _clock);

        priceApplied.ShouldBeTrue();
        product.Price.ShouldBe(new Money(150m, "BRL"));
        product.Sellable.ShouldBeTrue();
    }

    [Fact]
    public void Should_advance_its_own_version_only_when_an_event_was_applied()
    {
        var product = NewProduct();
        var before = product.Version;

        product.ApplyPrice(new Money(5m, "BRL"), 1, _clock);
        product.ApplyPrice(new Money(5m, "BRL"), 2, _clock);

        product.Version.ShouldBe(before + 1);
    }

    [Fact]
    public void Should_accept_a_sellable_product_priced_in_the_carts_currency()
    {
        var product = NewProduct();
        product.ApplyStatus(sellable: true, 2, _clock);

        product.EnsureBuyableIn("BRL").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_refuse_a_product_that_is_not_sellable()
    {
        var result = NewProduct().EnsureBuyableIn("BRL");

        result.ShouldFail().Code.ShouldBe("CART_PRODUCT_NOT_AVAILABLE");
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        result.ShouldFail().Field.ShouldBe("productId");
    }

    [Fact]
    public void Should_refuse_a_product_priced_in_another_currency_and_say_which_one_the_cart_uses()
    {
        var product = NewProduct(currency: "USD");
        product.ApplyStatus(sellable: true, 2, _clock);

        var result = product.EnsureBuyableIn("BRL");

        result.ShouldFail().Code.ShouldBe("CART_CURRENCY_MISMATCH");
        result.ShouldFail().ShouldHaveParameters()["currency"].ShouldBe("BRL");
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("SKU-0001", "")]
    public void Should_refuse_to_register_a_product_without_sku_or_name(string sku, string name)
    {
        Should.Throw<ArgumentException>(() =>
            CatalogProduct.Register(Guid.CreateVersion7(), sku, name, new Money(1m, "BRL"), 1, _clock)
        );
    }
}
