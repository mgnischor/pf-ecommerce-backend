using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Removes a line from a cart. A cart that is not the caller's does not exist (404); one that is no longer active
/// conflicts (409); a line that is not in the cart (for instance because it was already removed) is <c>404</c>.
/// </summary>
internal sealed class RemoveCartItemHandler(
    IShoppingCartRepository carts,
    ICatalogProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cart after the change, or the violated rule.</returns>
    public async Task<Result<CartView>> HandleAsync(RemoveCartItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await carts.GetByIdAsync(command.CartId, cancellationToken);
        if (cart is null || cart.CustomerId != command.CustomerId)
        {
            return Result<CartView>.Failure(CartErrors.NotFound);
        }

        var removed = cart.RemoveItem(command.ItemId, timeProvider);
        if (removed.IsFailure)
        {
            return Result<CartView>.Failure(removed.Error);
        }

        carts.Update(cart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var catalog = await products.GetManyAsync([.. cart.Items.Select(item => item.ProductId)], cancellationToken);
        return Result<CartView>.Success(cart.ToView(catalog));
    }
}
