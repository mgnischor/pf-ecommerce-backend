using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Adds a product to a cart. The price is never taken from the client: the product must be sellable in the cart's
/// currency according to the Cart's catalog view (BR-CRT-005). Errors come in a fixed order: a cart that is not the
/// caller's does not exist (404), a cart that is no longer active conflicts (409), then the request is validated (422).
/// Adding is not idempotent by nature, since a retry would add the units again; two concurrent changes are arbitrated by
/// the cart's version, and the loser is answered <c>409</c>.
/// </summary>
internal sealed class AddCartItemHandler(
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
    public async Task<Result<CartView>> HandleAsync(AddCartItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await carts.GetByIdAsync(command.CartId, cancellationToken);
        if (cart is null || cart.CustomerId != command.CustomerId)
        {
            return Result<CartView>.Failure(CartErrors.NotFound);
        }

        var active = cart.EnsureActive();
        if (active.IsFailure)
        {
            return Result<CartView>.Failure(active.Error);
        }

        var ids = new HashSet<Guid>(cart.Items.Select(item => item.ProductId)) { command.ProductId };
        var catalog = await products.GetManyAsync(ids, cancellationToken);

        var buyable = catalog.TryGetValue(command.ProductId, out var product)
            ? product.EnsureBuyableIn(cart.Currency)
            : Result.Failure(CartErrors.ProductNotAvailable);
        if (buyable.IsFailure)
        {
            return Result<CartView>.Failure(buyable.Error);
        }

        var added = cart.AddItem(command.ProductId, command.Quantity, timeProvider);
        if (added.IsFailure)
        {
            return Result<CartView>.Failure(added.Error);
        }

        carts.Update(cart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CartView>.Success(cart.ToView(catalog));
    }
}
