using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Application;

/// <summary>
/// Records a manual stock movement (BR-INV-001 to BR-INV-003). Idempotent: the movement stores the client's
/// <c>Idempotency-Key</c>, so a retry of the same adjustment answers with the item as it is and never adjusts twice,
/// while the same key with a different adjustment is rejected. The replay check runs before the version check
/// because a retry necessarily carries the version the original request read, which the original already advanced.
/// </summary>
internal sealed class AdjustStockHandler(
    IInventoryItemRepository items,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inventory item after the adjustment, or the violated rule.</returns>
    public async Task<Result<InventoryItemView>> HandleAsync(
        AdjustStockCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
        {
            return Result<InventoryItemView>.Failure(sku.Error);
        }

        var item = await items.FindBySkuAsync(sku.Value, cancellationToken);
        if (item is null)
        {
            return Result<InventoryItemView>.Failure(InventoryErrors.NotFound);
        }

        var previous = await items.FindMovementAsync(item.Id, command.IdempotencyKey, cancellationToken);
        if (previous is not null)
        {
            return previous.IsEquivalentTo(command.Delta, command.ReasonCode)
                ? Result<InventoryItemView>.Success(item.ToView())
                : Result<InventoryItemView>.Failure(InventoryErrors.IdempotencyKeyReused);
        }

        if (command.ExpectedVersion != item.Version)
        {
            return Result<InventoryItemView>.Failure(InventoryErrors.VersionMismatch);
        }

        var adjusted = item.Adjust(
            command.Delta,
            command.ReasonCode,
            command.ActorId,
            command.IdempotencyKey,
            timeProvider
        );
        if (adjusted.IsFailure)
        {
            return Result<InventoryItemView>.Failure(adjusted.Error);
        }

        items.Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InventoryItemView>.Success(item.ToView());
    }
}
