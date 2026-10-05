using Portfolio.Cart.Application;
using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Cart.Application;

[Trait("Rule", "BR-CRT-005")]
public sealed class SyncCatalogProductHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly FakeInbox _inbox = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly SyncCatalogProductHandler _handler;
    private readonly Guid _productId = Guid.CreateVersion7();

    public SyncCatalogProductHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_inbox);
        _handler = new SyncCatalogProductHandler(_products, _unitOfWork, _inbox, _clock);
    }

    private Task<Result<bool>> SyncAsync(
        CatalogProductEventKind kind,
        int version,
        Guid? messageId = null,
        string? sku = "CAF-600-PRT",
        string? name = "Cafeteira Elétrica",
        Money? price = null,
        string? status = null
    ) =>
        _handler.HandleAsync(
            new SyncCatalogProductCommand(
                messageId ?? Guid.CreateVersion7(),
                kind,
                _productId,
                version,
                sku,
                name,
                price,
                status
            ),
            TestContext.Current.CancellationToken
        );

    private Task<Result<bool>> CreateAsync(Guid? messageId = null) =>
        SyncAsync(CatalogProductEventKind.Created, 1, messageId, price: new Money(100m, "BRL"));

    private CatalogProduct Stored() => _products.Products.ShouldHaveSingleItem();

    [Fact]
    public async Task Should_start_the_view_of_a_created_product_as_not_sellable()
    {
        var result = await CreateAsync();

        result.Value.ShouldBeTrue();
        var product = Stored();
        (product.Id, product.Sku, product.Name, product.Sellable).ShouldBe(
            (_productId, "CAF-600-PRT", "Cafeteira Elétrica", false)
        );
        product.Price.ShouldBe(new Money(100m, "BRL"));
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_do_nothing_when_the_same_message_is_delivered_again()
    {
        var messageId = Guid.CreateVersion7();
        await CreateAsync(messageId);

        var redelivery = await CreateAsync(messageId);

        redelivery.Value.ShouldBeFalse();
        _products.Products.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_not_create_the_view_twice_when_a_second_message_reports_the_same_creation()
    {
        await CreateAsync();

        var second = await CreateAsync();

        second.Value.ShouldBeTrue();
        _products.Products.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_follow_the_price_after_the_product_was_created()
    {
        await CreateAsync();

        await SyncAsync(CatalogProductEventKind.PriceChanged, 2, price: new Money(120m, "BRL"));

        Stored().Price.ShouldBe(new Money(120m, "BRL"));
        _unitOfWork.SaveCalls.ShouldBe(2);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("Active", true)]
    [InlineData("discontinued", false)]
    [InlineData("draft", false)]
    public async Task Should_make_the_product_sellable_only_when_it_reaches_active(string status, bool sellable)
    {
        await CreateAsync();

        await SyncAsync(CatalogProductEventKind.StatusChanged, 2, status: status);

        Stored().Sellable.ShouldBe(sellable);
    }

    [Fact]
    public async Task Should_stop_selling_a_deleted_product()
    {
        await CreateAsync();
        await SyncAsync(CatalogProductEventKind.StatusChanged, 2, status: "active");

        await SyncAsync(CatalogProductEventKind.Deleted, 3);

        Stored().Sellable.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_not_let_a_stale_event_overwrite_a_newer_one_when_events_arrive_out_of_order()
    {
        await CreateAsync();
        await SyncAsync(CatalogProductEventKind.PriceChanged, 4, price: new Money(150m, "BRL"));

        var stale = await SyncAsync(CatalogProductEventKind.PriceChanged, 3, price: new Money(999m, "BRL"));

        stale.Value.ShouldBeTrue();
        Stored().Price.ShouldBe(new Money(150m, "BRL"));
    }

    [Theory]
    [InlineData("PriceChanged")]
    [InlineData("StatusChanged")]
    [InlineData("Deleted")]
    public async Task Should_report_an_unknown_product_as_retriable_and_persist_nothing_when_its_creation_has_not_arrived(
        string kindName
    )
    {
        var result = await SyncAsync(
            Enum.Parse<CatalogProductEventKind>(kindName),
            2,
            price: new Money(1m, "BRL"),
            status: "active"
        );

        result.ShouldFail().ShouldBe(SyncCatalogProductHandler.UnknownProduct);
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        _products.Products.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_apply_an_event_again_after_its_first_attempt_failed_because_the_creation_was_behind()
    {
        var messageId = Guid.CreateVersion7();
        var first = await SyncAsync(CatalogProductEventKind.PriceChanged, 2, messageId, price: new Money(120m, "BRL"));
        _inbox.EndDelivery(); // the failed delivery ended without a commit
        await CreateAsync();

        // The failed attempt committed nothing, so the redelivery of the same message is handled, not skipped.
        var retry = await SyncAsync(CatalogProductEventKind.PriceChanged, 2, messageId, price: new Money(120m, "BRL"));

        first.IsFailure.ShouldBeTrue();
        retry.Value.ShouldBeTrue();
        Stored().Price.ShouldBe(new Money(120m, "BRL"));
    }

    [Theory]
    [InlineData("Created", null, "Name", true)]
    [InlineData("Created", "SKU-0001", null, true)]
    [InlineData("Created", "SKU-0001", "Name", false)]
    [InlineData("PriceChanged", null, null, false)]
    [InlineData("StatusChanged", null, null, false)]
    public async Task Should_reject_an_event_missing_the_data_its_kind_requires_as_malformed(
        string kindName,
        string? sku,
        string? name,
        bool hasPrice
    )
    {
        var result = await SyncAsync(
            Enum.Parse<CatalogProductEventKind>(kindName),
            1,
            sku: sku,
            name: name,
            price: hasPrice ? new Money(1m, "BRL") : null,
            status: null
        );

        result.ShouldFail().ShouldBe(SyncCatalogProductHandler.MalformedEvent);
        _inbox.Begun.ShouldBe(0);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_an_event_without_a_product_or_a_valid_version_as_malformed()
    {
        var noVersion = await SyncAsync(CatalogProductEventKind.Deleted, 0);
        var noProduct = await _handler.HandleAsync(
            new SyncCatalogProductCommand(
                Guid.CreateVersion7(),
                CatalogProductEventKind.Deleted,
                Guid.Empty,
                1,
                null,
                null,
                null,
                null
            ),
            TestContext.Current.CancellationToken
        );

        noVersion.ShouldFail().ShouldBe(SyncCatalogProductHandler.MalformedEvent);
        noProduct.ShouldFail().ShouldBe(SyncCatalogProductHandler.MalformedEvent);
    }
}
