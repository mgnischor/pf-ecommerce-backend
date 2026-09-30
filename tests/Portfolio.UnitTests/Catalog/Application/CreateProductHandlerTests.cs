using Portfolio.Catalog.Application;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Application;

public sealed class CreateProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CreateProductHandler _handler;

    public CreateProductHandlerTests()
    {
        _handler = new CreateProductHandler(_products, _unitOfWork, _clock);
    }

    private static CreateProductCommand ValidCommand(string sku = "CAF-600-PRT") =>
        new("Cafeteira Elétrica", sku, 189.90m, "BRL", "Com filtro permanente.");

    [Fact]
    public async Task Should_persist_a_draft_product_and_return_its_identifier_when_the_request_is_valid()
    {
        var result = await _handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        var product = _products.Products.ShouldHaveSingleItem();
        result.Value.ShouldBe(product.Id);
        product.Status.ShouldBe(ProductStatus.Draft);
        product.Sku.Value.ShouldBe("CAF-600-PRT");
        product.Price.ShouldBe(new Money(189.90m, "BRL"));
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CAT-005")]
    public async Task Should_reject_a_duplicate_sku_even_when_it_differs_only_by_case_and_persist_nothing()
    {
        await _handler.HandleAsync(ValidCommand("caf-600-prt"), TestContext.Current.CancellationToken);

        var replay = await _handler.HandleAsync(ValidCommand("CAF-600-PRT"), TestContext.Current.CancellationToken);

        replay.ShouldFail().Code.ShouldBe("PRODUCT_SKU_ALREADY_EXISTS");
        replay.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        replay.ShouldFail().RuleId.ShouldBe("BR-CAT-005");
        _products.Products.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, "SKU_REQUIRED")]
    [InlineData("AB", "SKU_LENGTH")]
    [InlineData("ABC 1234", "SKU_INVALID_CHARACTERS")]
    [Trait("Rule", "BR-CAT-004")]
    public async Task Should_reject_an_invalid_sku_and_persist_nothing(string? sku, string expectedCode)
    {
        var command = ValidCommand() with { Sku = sku };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe(expectedCode);
        AssertNothingPersisted();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("BR")]
    [InlineData("B2N")]
    public async Task Should_reject_an_invalid_currency_and_persist_nothing(string? currency)
    {
        var command = ValidCommand() with { Currency = currency };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
        AssertNothingPersisted();
    }

    [Fact]
    [Trait("Rule", "BR-CAT-002")]
    public async Task Should_reject_a_price_that_is_not_positive_and_persist_nothing()
    {
        var command = ValidCommand() with { Price = 0m };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
        AssertNothingPersisted();
    }

    [Fact]
    [Trait("Rule", "BR-CAT-001")]
    public async Task Should_reject_an_invalid_name_and_persist_nothing()
    {
        var command = ValidCommand() with { Name = "ab" };

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_LENGTH");
        AssertNothingPersisted();
    }

    private void AssertNothingPersisted()
    {
        _products.Products.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
