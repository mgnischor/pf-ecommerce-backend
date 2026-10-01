using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Application;

/// <summary>Reads the stock level of a SKU (BR-INV-004).</summary>
internal sealed class GetInventoryItemHandler(IInventoryItemRepository items)
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The inventory item, or the reason it cannot be read.</returns>
    public async Task<Result<InventoryItemView>> HandleAsync(
        GetInventoryItemQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        var sku = Sku.Create(query.Sku);
        if (sku.IsFailure)
        {
            return Result<InventoryItemView>.Failure(sku.Error);
        }

        var item = await items.FindBySkuAsync(sku.Value, cancellationToken);

        return item is null
            ? Result<InventoryItemView>.Failure(InventoryErrors.NotFound)
            : Result<InventoryItemView>.Success(item.ToView());
    }
}
