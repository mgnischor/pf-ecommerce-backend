using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Application;

/// <summary>
/// Opens the inventory item of a newly created product when the Catalog's <c>ProductCreated</c> event arrives
/// (BR-INV-008). Delivery is at least once, so the handler is idempotent twice over: the inbox row for the message is
/// committed in the same transaction as the item (a redelivery finds it and does nothing), and an item that already
/// exists for the SKU, for example opened by hand through the API, is left alone and still recorded as handled.
/// </summary>
internal sealed class OpenInventoryItemOnProductCreatedHandler(
    IInventoryItemRepository items,
    IUnitOfWork unitOfWork,
    IInbox inbox,
    TimeProvider timeProvider
)
{
    /// <summary>Stable consumer name: the inbox key, the queue name and the telemetry label.</summary>
    public const string ConsumerName = "inventory.open-item-on-product-created";

    /// <summary>Executes the command.</summary>
    /// <param name="command">The event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the message was handled now, <c>false</c> when it was a duplicate, or the violated rule.</returns>
    public async Task<Result<bool>> HandleAsync(
        OpenInventoryItemOnProductCreatedCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
        {
            return Result<bool>.Failure(sku.Error);
        }

        if (!await inbox.TryBeginAsync(command.MessageId, ConsumerName, cancellationToken))
        {
            return Result<bool>.Success(false);
        }

        if (await items.FindBySkuAsync(sku.Value, cancellationToken) is null)
        {
            await items.AddAsync(InventoryItem.Open(sku.Value, timeProvider), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
