using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Application;

/// <summary>
/// Opens the inventory item of a SKU (BR-INV-008). Retrying the same request is safe: the existence check turns a
/// replay into a conflict instead of a duplicate, and the unique index on <c>sku</c> closes the race between two
/// concurrent requests. Whether the SKU exists in the catalog is not checked here: the contexts only talk through
/// events, so the item is expected to be opened by the catalog's <c>ProductCreated</c> integration event once the
/// messaging infrastructure exists.
/// </summary>
internal sealed class OpenInventoryItemHandler(
    IInventoryItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new, empty inventory item, or the violated rule.</returns>
    public async Task<Result<InventoryItemView>> HandleAsync(
        OpenInventoryItemCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
        {
            return Result<InventoryItemView>.Failure(sku.Error);
        }

        if (await items.FindBySkuAsync(sku.Value, cancellationToken) is not null)
        {
            return Result<InventoryItemView>.Failure(InventoryErrors.ItemAlreadyExists);
        }

        var item = InventoryItem.Open(sku.Value, timeProvider);
        await items.AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InventoryItemView>.Success(item.ToView());
    }
}
