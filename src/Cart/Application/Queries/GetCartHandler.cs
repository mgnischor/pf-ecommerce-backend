using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Reads a cart with prices computed on the server from the catalog view (BR-CRT-005). A cart of another customer
/// answers exactly like one that does not exist, so its existence is not revealed.
/// </summary>
internal sealed class GetCartHandler(IShoppingCartRepository carts, ICatalogProductRepository products)
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cart, or the reason it cannot be read.</returns>
    public async Task<Result<CartView>> HandleAsync(GetCartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cart = await carts.GetByIdAsync(query.CartId, cancellationToken);
        if (cart is null || cart.CustomerId != query.CustomerId)
        {
            return Result<CartView>.Failure(CartErrors.NotFound);
        }

        var catalog = await products.GetManyAsync([.. cart.Items.Select(item => item.ProductId)], cancellationToken);
        return Result<CartView>.Success(cart.ToView(catalog));
    }
}
