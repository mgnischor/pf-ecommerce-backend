using Portfolio.Cart.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Cart.Infrastructure;

/// <summary>
/// Consumes the Catalog's product events (<c>catalog.*</c>) and keeps the Cart's own view of the catalog current
/// (BR-CRT-005). The queue belongs to this consumer, so the Cart keeps receiving events while the Catalog is down or
/// redeployed. A catalog event of a kind the Cart does not use is acknowledged and ignored.
/// </summary>
internal sealed class CatalogProductsConsumer : JsonMessageConsumer<CatalogProductMessage>
{
    private const string ProductCreated = "catalog.product-created";
    private const string ProductPriceChanged = "catalog.product-price-changed";
    private const string ProductStatusChanged = "catalog.product-status-changed";
    private const string ProductDeleted = "catalog.product-deleted";

    /// <inheritdoc />
    public override string Name => SyncCatalogProductHandler.ConsumerName;

    /// <inheritdoc />
    public override string BindingKey => "catalog.*";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(
        ReceivedMessage message,
        CatalogProductMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var kind = message.RoutingKey switch
        {
            ProductCreated => CatalogProductEventKind.Created,
            ProductPriceChanged => CatalogProductEventKind.PriceChanged,
            ProductStatusChanged => CatalogProductEventKind.StatusChanged,
            ProductDeleted => CatalogProductEventKind.Deleted,
            _ => (CatalogProductEventKind?)null,
        };
        if (kind is null)
        {
            return true;
        }

        var handler = services.GetRequiredService<SyncCatalogProductHandler>();
        var result = await handler.HandleAsync(
            new SyncCatalogProductCommand(
                message.MessageId,
                kind.Value,
                payload.AggregateId,
                payload.AggregateVersion,
                payload.Sku,
                payload.Name,
                kind == CatalogProductEventKind.PriceChanged ? payload.NewPrice : payload.Price,
                payload.To
            ),
            cancellationToken
        );

        return result.IsFailure ? throw FailureOf(result.Error) : result.Value;
    }

    // An event for a product whose creation has not been processed yet is retried: queues are independent, so the
    // creation may simply be behind. Anything else is malformed and no retry can fix it.
    private static Exception FailureOf(Error error) =>
        error.Type == ErrorType.NotFound
            ? new InvalidOperationException("The product's creation event has not been processed yet.")
            : new PoisonMessageException("The catalog event lacks data the Cart needs.");
}
