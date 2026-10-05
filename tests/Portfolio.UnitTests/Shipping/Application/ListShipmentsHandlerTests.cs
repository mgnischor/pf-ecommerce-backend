using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Application;
using Portfolio.Shipping.Domain;
using Portfolio.UnitTests.Shipping.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Shipping.Application;

[Trait("Rule", "BR-SHP-004")]
public sealed class ListShipmentsHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeOrderReferenceRepository _references = new();
    private readonly FakeShipmentRepository _shipments = new();
    private readonly HmacPageCursorCodec _cursors = new(
        Options.Create(
            new PageCursorOptions { CursorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }
        ),
        environmentAllowsEphemeralKey: false,
        NullLogger<HmacPageCursorCodec>.Instance
    );
    private readonly ListShipmentsHandler _handler;
    private readonly Guid _order = Guid.CreateVersion7();
    private readonly Guid _customer = Guid.CreateVersion7();

    public ListShipmentsHandlerTests()
    {
        _handler = new ListShipmentsHandler(_references, _shipments, _cursors);
        _references.Seed(OrderReference.Record(_order, _customer, "PF-2026-000001", _clock).Value);
    }

    private Task<Result<ShipmentPage>> ListAsync(
        int limit = 20,
        string? cursor = null,
        Guid? order = null,
        Guid? customer = null
    ) =>
        _handler.HandleAsync(
            new ListShipmentsQuery(customer ?? _customer, order ?? _order, limit, cursor),
            TestContext.Current.CancellationToken
        );

    private void SeedShipments(int count)
    {
        for (var index = 0; index < count; index++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _shipments.Seed(ShipmentBuilder.New().ForOrder(_order, _customer).Build(_clock));
        }
    }

    [Fact]
    public async Task Should_answer_an_empty_page_for_an_order_of_the_caller_that_has_no_shipment_yet()
    {
        var result = await ListAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
        (result.Value.HasMore, result.Value.NextCursor).ShouldBe((false, null));
    }

    [Fact]
    public async Task Should_list_the_shipment_of_the_order_with_its_carrier_data()
    {
        var shipment = ShipmentBuilder
            .New()
            .ForOrder(_order, _customer)
            .WithStatus(ShipmentStatus.InTransit)
            .Build(_clock);
        _shipments.Seed(shipment);

        var result = await ListAsync();

        var view = result.Value.Items.ShouldHaveSingleItem();
        (view.Id, view.OrderId, view.Status, view.Carrier, view.TrackingCode).ShouldBe(
            (shipment.Id, _order, ShipmentStatusView.InTransit, "Correios", "BR123456789")
        );
        view.EstimatedDeliveryDate.ShouldBe(shipment.EstimatedDeliveryDate);
    }

    [Fact]
    public async Task Should_answer_not_found_for_an_order_of_another_customer_exactly_like_an_unknown_one()
    {
        var foreign = await ListAsync(customer: Guid.CreateVersion7());
        var unknown = await ListAsync(order: Guid.CreateVersion7());

        foreign.ShouldFail().ShouldBe(unknown.ShouldFail());
        foreign.ShouldFail().Code.ShouldBe("ORDER_NOT_FOUND");
        foreign.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_never_show_a_shipment_that_belongs_to_another_customer_even_for_the_same_order_id()
    {
        _shipments.Seed(ShipmentBuilder.New().ForOrder(_order, Guid.CreateVersion7()).Build(_clock));

        var result = await ListAsync();

        result.Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_trim_the_page_and_issue_a_cursor_that_continues_after_the_last_shipment()
    {
        SeedShipments(3);

        var first = await ListAsync(limit: 2);
        var second = await ListAsync(limit: 2, cursor: first.Value.NextCursor);

        first.Value.Items.Count.ShouldBe(2);
        first.Value.HasMore.ShouldBeTrue();
        second.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(_shipments.Shipments[2].Id);
        second.Value.HasMore.ShouldBeFalse();
        _shipments.LastAfter.ShouldNotBeNull().Id.ShouldBe(_shipments.Shipments[1].Id);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(100, 101)]
    [InlineData(9999, 101)]
    public async Task Should_clamp_the_page_size_to_the_documented_range(int limit, int expectedRowsRequested)
    {
        await ListAsync(limit);

        _shipments.LastLimit.ShouldBe(expectedRowsRequested);
    }

    [Fact]
    public async Task Should_reject_a_forged_cursor_as_a_malformed_request()
    {
        var result = await ListAsync(cursor: "bm90LWEtY3Vyc29y.AAAA");

        result.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
        result.ShouldFail().Type.ShouldBe(ErrorType.BadRequest);
    }

    [Fact]
    public async Task Should_reject_a_cursor_issued_for_another_order_or_to_another_customer()
    {
        SeedShipments(3);
        var first = await ListAsync(limit: 2);
        var otherOrder = Guid.CreateVersion7();
        _references.Seed(OrderReference.Record(otherOrder, _customer, "PF-2026-000002", _clock).Value);
        var otherCustomer = Guid.CreateVersion7();
        _references.Seed(OrderReference.Record(_order, otherCustomer, "PF-2026-000001", _clock).Value);

        var forOtherOrder = await ListAsync(limit: 2, cursor: first.Value.NextCursor, order: otherOrder);

        forOtherOrder.ShouldFail().Code.ShouldBe("PAGE_CURSOR_INVALID");
    }
}
