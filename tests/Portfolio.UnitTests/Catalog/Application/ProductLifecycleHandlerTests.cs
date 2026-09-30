using Portfolio.Catalog.Application;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Catalog.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Application;

[Trait("Rule", "BR-CAT-003")]
public sealed class ActivateProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ActivateProductHandler _handler;

    public ActivateProductHandlerTests()
    {
        _handler = new ActivateProductHandler(_products, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Should_activate_a_draft_product_and_persist_it()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new ActivateProductCommand(product.Id),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Active);
        _products.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_report_not_found_and_persist_nothing_when_the_product_does_not_exist()
    {
        var result = await _handler.HandleAsync(
            new ActivateProductCommand(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_replayed_request_as_a_conflict_without_applying_it_twice()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);
        var command = new ActivateProductCommand(product.Id);
        await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var replay = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        replay.ShouldFail().Code.ShouldBe("PRODUCT_INVALID_STATUS_TRANSITION");
        product.DomainEvents.OfType<ProductStatusChanged>().Count().ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }
}

[Trait("Rule", "BR-CAT-003")]
public sealed class DiscontinueProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly DiscontinueProductHandler _handler;

    public DiscontinueProductHandlerTests()
    {
        _handler = new DiscontinueProductHandler(_products, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Should_discontinue_an_active_product_and_persist_it()
    {
        var product = ProductBuilder.New().WithStatus(ProductStatus.Active).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new DiscontinueProductCommand(product.Id),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Discontinued);
        _products.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_report_not_found_and_persist_nothing_when_the_product_does_not_exist()
    {
        var result = await _handler.HandleAsync(
            new DiscontinueProductCommand(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_draft_product_as_a_conflict_and_persist_nothing()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new DiscontinueProductCommand(product.Id),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_INVALID_STATUS_TRANSITION");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}

[Trait("Rule", "BR-CAT-002")]
public sealed class ChangeProductPriceHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ChangeProductPriceHandler _handler;

    public ChangeProductPriceHandlerTests()
    {
        _handler = new ChangeProductPriceHandler(_products, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Should_change_the_price_and_persist_it()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new ChangeProductPriceCommand(product.Id, 120m, "BRL"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        product.Price.ShouldBe(new Money(120m, "BRL"));
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_raise_a_single_price_changed_event_when_the_same_request_is_retried()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);
        _products.Seed(product);
        var command = new ChangeProductPriceCommand(product.Id, 120m, "BRL");

        var first = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);
        var retry = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        retry.IsSuccess.ShouldBeTrue();
        product.DomainEvents.OfType<ProductPriceChanged>().Count().ShouldBe(1);
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    public async Task Should_report_not_found_and_persist_nothing_when_the_product_does_not_exist()
    {
        var result = await _handler.HandleAsync(
            new ChangeProductPriceCommand(Guid.CreateVersion7(), 10m, "BRL"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("R$")]
    public async Task Should_reject_an_invalid_currency_and_persist_nothing(string? currency)
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new ChangeProductPriceCommand(product.Id, 10m, currency),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_price_that_is_not_positive_and_persist_nothing()
    {
        var product = ProductBuilder.New().WithPrice(100m).Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new ChangeProductPriceCommand(product.Id, -1m, "BRL"),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_PRICE_MUST_BE_POSITIVE");
        product.Price.ShouldBe(new Money(100m, "BRL"));
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
