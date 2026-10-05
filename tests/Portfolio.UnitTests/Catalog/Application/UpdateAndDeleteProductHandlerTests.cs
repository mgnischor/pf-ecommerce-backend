using Portfolio.Catalog.Application;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Catalog.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Application;

[Trait("Rule", "BR-CAT-001")]
public sealed class UpdateProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly UpdateProductHandler _handler;

    public UpdateProductHandlerTests()
    {
        _handler = new UpdateProductHandler(_products, _unitOfWork, _clock);
    }

    private static UpdateProductCommand Command(Product product, string? name = null, int? version = null) =>
        new(product.Id, ChangeName: name is not null, name, false, null, version ?? product.Version);

    [Fact]
    public async Task Should_rename_the_product_persist_it_and_return_the_new_version()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            Command(product, "Cafeteira Premium"),
            TestContext.Current.CancellationToken
        );

        result.Value.Name.ShouldBe("Cafeteira Premium");
        result.Value.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        _products.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_succeed_without_persisting_when_the_request_asks_for_what_the_product_already_holds()
    {
        var product = ProductBuilder.New().WithName("Cafeteira").Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(Command(product, "Cafeteira"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(AggregateRoot.InitialVersion);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_report_not_found_when_the_product_does_not_exist()
    {
        var command = new UpdateProductCommand(Guid.CreateVersion7(), true, "Cafeteira", false, null, 1);

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task Should_fail_the_precondition_and_change_nothing_when_the_version_is_stale_or_malformed(
        int? version
    )
    {
        var product = ProductBuilder.New().WithName("Cafeteira").Build(_clock);
        _products.Seed(product);
        var command = new UpdateProductCommand(product.Id, true, "Outra", false, null, version);

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Type.ShouldBe(ErrorType.PreconditionFailed);
        result.ShouldFail().Code.ShouldBe("PRODUCT_VERSION_MISMATCH");
        product.Name.ShouldBe("Cafeteira");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_an_explicit_null_name_and_persist_nothing()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);
        var command = new UpdateProductCommand(product.Id, true, null, false, null, product.Version);

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.ShouldFail().Code.ShouldBe("PRODUCT_NAME_REQUIRED");
        result.ShouldFail().Type.ShouldBe(ErrorType.Validation);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_clear_the_description_when_it_is_named_null()
    {
        var product = ProductBuilder.New().WithDescription("Descrição original").Build(_clock);
        _products.Seed(product);
        var command = new UpdateProductCommand(product.Id, false, null, true, null, product.Version);

        var result = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        result.Value.Description.ShouldBeNull();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }
}

[Trait("Rule", "BR-CAT-007")]
public sealed class DeleteProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly DeleteProductHandler _handler;

    public DeleteProductHandlerTests()
    {
        _handler = new DeleteProductHandler(_products, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Should_logically_delete_the_product_and_keep_the_idempotency_key()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new DeleteProductCommand(product.Id, "key-0001", product.Version),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        product.IsDeleted.ShouldBeTrue();
        product.DeletionKey.ShouldBe("key-0001");
        _products.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_release_the_sku_so_another_product_may_use_it()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        await _handler.HandleAsync(
            new DeleteProductCommand(product.Id, "key-0001", product.Version),
            TestContext.Current.CancellationToken
        );

        (await _products.FindBySkuAsync(product.Sku, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    [Trait("Rule", "BR-CAT-008")]
    public async Task Should_answer_a_retry_after_the_deletion_as_the_success_it_was()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);
        var command = new DeleteProductCommand(product.Id, "key-0001", product.Version);
        await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // The retry carries the version the original read, which the original already advanced.
        var retry = await _handler.HandleAsync(command, TestContext.Current.CancellationToken);

        retry.IsSuccess.ShouldBeTrue();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CAT-008")]
    public async Task Should_report_not_found_when_the_product_was_deleted_by_a_request_with_another_key()
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);
        await _handler.HandleAsync(
            new DeleteProductCommand(product.Id, "key-0001", product.Version),
            TestContext.Current.CancellationToken
        );

        var other = await _handler.HandleAsync(
            new DeleteProductCommand(product.Id, "key-0002", product.Version),
            TestContext.Current.CancellationToken
        );

        other.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        other.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_report_not_found_when_the_product_never_existed()
    {
        var result = await _handler.HandleAsync(
            new DeleteProductCommand(Guid.CreateVersion7(), "key-0001", 1),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_NOT_FOUND");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task Should_fail_the_precondition_and_delete_nothing_when_the_version_is_stale_or_malformed(
        int? version
    )
    {
        var product = ProductBuilder.New().Build(_clock);
        _products.Seed(product);

        var result = await _handler.HandleAsync(
            new DeleteProductCommand(product.Id, "key-0001", version),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_VERSION_MISMATCH");
        result.ShouldFail().Type.ShouldBe(ErrorType.PreconditionFailed);
        product.IsDeleted.ShouldBeFalse();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
