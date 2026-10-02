using Portfolio.Inventory.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>
/// Consumes the Catalog's <c>catalog.product-created</c> event and opens the inventory item of the new SKU. The queue
/// belongs to this consumer, so Inventory keeps receiving events while the Catalog is down or redeployed.
/// </summary>
internal sealed class ProductCreatedConsumer : JsonMessageConsumer<ProductCreatedMessage>
{
    /// <inheritdoc />
    public override string Name => OpenInventoryItemOnProductCreatedHandler.ConsumerName;

    /// <inheritdoc />
    public override string BindingKey => "catalog.product-created";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(
        ReceivedMessage message,
        ProductCreatedMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var handler = services.GetRequiredService<OpenInventoryItemOnProductCreatedHandler>();

        var result = await handler.HandleAsync(
            new OpenInventoryItemOnProductCreatedCommand(message.MessageId, payload.Sku),
            cancellationToken
        );

        // A SKU the Catalog published but Inventory's rules reject cannot be fixed by retrying.
        return result.IsFailure
            ? throw new PoisonMessageException("The product's SKU is not valid for Inventory.")
            : result.Value;
    }
}
