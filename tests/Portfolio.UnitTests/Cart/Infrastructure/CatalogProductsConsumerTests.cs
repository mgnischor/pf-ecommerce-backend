using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Cart.Application;
using Portfolio.Cart.Domain;
using Portfolio.Cart.Infrastructure;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.UnitTests.Cart.Application;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Cart.Infrastructure;

/// <summary>
/// The Cart's consumer of the Catalog's events: it reads each event through its own message type, applies it, retries
/// what is only early, and dead-letters what can never be handled (BR-CRT-005).
/// </summary>
[Trait("Rule", "BR-CRT-005")]
public sealed class CatalogProductsConsumerTests
{
    private static readonly Guid ProductId = Guid.Parse("0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57");

    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly CatalogProductsConsumer _consumer = new();
    private readonly FakeInbox _inbox = new();
    private readonly ServiceProvider _services;

    public CatalogProductsConsumerTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ICatalogProductRepository>(_products)
            .AddSingleton<IUnitOfWork>(new FakeUnitOfWork(_inbox))
            .AddSingleton<IInbox>(_inbox)
            .AddSingleton<TimeProvider>(_clock)
            .AddScoped<SyncCatalogProductHandler>()
            .BuildServiceProvider();
    }

    private Task<bool> ConsumeAsync(string routingKey, string json, Guid? messageId = null)
    {
        var message = new ReceivedMessage(
            messageId ?? Guid.CreateVersion7(),
            routingKey,
            CorrelationId: null,
            Redelivered: false,
            Attempt: 1,
            Encoding.UTF8.GetBytes(json)
        );

        return _consumer.ConsumeAsync(message, _services, TestContext.Current.CancellationToken);
    }

    private static string Created(int version = 1, string price = "189.90") =>
        $$"""
            {"eventId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e01","aggregateId":"{{ProductId}}","aggregateVersion":{{version}},
             "occurredAt":"2026-10-02T12:00:00+00:00","name":"Cafeteira Elétrica 600ml","sku":"CAF-600-PRT",
             "price":{"amount":"{{price}}","currency":"BRL"},"someFieldAddedLater":true}
            """;

    // Built with concatenation: a raw interpolated literal cannot hold the "}}" that closes two nested objects.
    private static string PriceChanged(int version, string oldAmount, string newAmount) =>
        "{\"aggregateId\":\""
        + ProductId
        + "\",\"aggregateVersion\":"
        + version
        + ",\"oldPrice\":{\"amount\":\""
        + oldAmount
        + "\",\"currency\":\"BRL\"},\"newPrice\":{\"amount\":\""
        + newAmount
        + "\",\"currency\":\"BRL\"}}";

    [Fact]
    public void Should_bind_one_queue_of_its_own_to_every_catalog_event()
    {
        _consumer.Name.ShouldBe("cart.sync-catalog-products");
        _consumer.BindingKey.ShouldBe("catalog.*");
    }

    [Fact]
    public async Task Should_create_the_catalog_view_from_a_product_created_event_ignoring_unknown_fields()
    {
        var handled = await ConsumeAsync("catalog.product-created", Created());

        handled.ShouldBeTrue();
        var product = _products.Products.ShouldHaveSingleItem();
        (product.Id, product.Sku, product.Name).ShouldBe((ProductId, "CAF-600-PRT", "Cafeteira Elétrica 600ml"));
        product.Price.Amount.ShouldBe(189.90m);
        product.Sellable.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_apply_the_new_price_of_a_price_changed_event_and_not_the_old_one()
    {
        await ConsumeAsync("catalog.product-created", Created());

        await ConsumeAsync(
            "catalog.product-price-changed",
            PriceChanged(version: 2, oldAmount: "189.90", newAmount: "159.90")
        );

        _products.Products.Single().Price.Amount.ShouldBe(159.90m);
    }

    [Fact]
    public async Task Should_make_the_product_sellable_on_a_status_changed_event_to_active()
    {
        await ConsumeAsync("catalog.product-created", Created());

        await ConsumeAsync(
            "catalog.product-status-changed",
            $$"""{"aggregateId":"{{ProductId}}","aggregateVersion":2,"from":"draft","to":"active"}"""
        );

        _products.Products.Single().Sellable.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_withdraw_the_product_on_a_deleted_event()
    {
        await ConsumeAsync("catalog.product-created", Created());
        await ConsumeAsync(
            "catalog.product-status-changed",
            $$"""{"aggregateId":"{{ProductId}}","aggregateVersion":2,"from":"draft","to":"active"}"""
        );

        await ConsumeAsync(
            "catalog.product-deleted",
            $$"""{"aggregateId":"{{ProductId}}","aggregateVersion":3,"sku":"CAF-600-PRT"}"""
        );

        _products.Products.Single().Sellable.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_acknowledge_and_ignore_a_catalog_event_it_does_not_use()
    {
        var handled = await ConsumeAsync(
            "catalog.category-created",
            """{"aggregateId":"0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57"}"""
        );

        handled.ShouldBeTrue();
        _products.Products.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_requeue_an_event_whose_product_creation_has_not_been_processed_yet()
    {
        var early = ConsumeAsync(
            "catalog.product-price-changed",
            PriceChanged(version: 2, oldAmount: "1.00", newAmount: "1.00")
        );

        // A plain exception requeues the message; PoisonMessageException would dead-letter it.
        var failure = await Should.ThrowAsync<InvalidOperationException>(() => early);

        failure.ShouldNotBeOfType<PoisonMessageException>();
    }

    [Fact]
    public async Task Should_dead_letter_a_creation_event_that_lacks_what_the_cart_needs()
    {
        await Should.ThrowAsync<PoisonMessageException>(() =>
            ConsumeAsync(
                "catalog.product-created",
                $$"""{"aggregateId":"{{ProductId}}","aggregateVersion":1,"sku":"CAF-600-PRT"}"""
            )
        );
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Should_dead_letter_a_body_that_is_not_an_event(string body)
    {
        await Should.ThrowAsync<PoisonMessageException>(() => ConsumeAsync("catalog.product-created", body));
    }

    [Fact]
    public async Task Should_report_a_redelivery_as_not_handled_now()
    {
        var messageId = Guid.CreateVersion7();
        await ConsumeAsync("catalog.product-created", Created(), messageId);

        var redelivery = await ConsumeAsync("catalog.product-created", Created(), messageId);

        redelivery.ShouldBeFalse();
        _products.Products.Count.ShouldBe(1);
    }
}
