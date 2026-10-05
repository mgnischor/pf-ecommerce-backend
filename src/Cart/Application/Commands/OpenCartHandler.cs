using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Opens the customer's cart (BR-CRT-001): a customer has at most one active cart, so asking again returns the one
/// that is open instead of creating a second, which also makes a retry safe. A cart already open in another currency is
/// a conflict. Two requests racing to open the first cart are closed by a partial unique index; the loser is answered
/// <c>409</c> and its retry finds the winner's cart.
/// </summary>
internal sealed class OpenCartHandler(
    IShoppingCartRepository carts,
    ICatalogProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The customer's active cart and whether it was created now, or the violated rule.</returns>
    public async Task<Result<OpenedCart>> HandleAsync(OpenCartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var money = Money.Create(0m, command.Currency);
        if (money.IsFailure)
        {
            return Result<OpenedCart>.Failure(money.Error);
        }

        var existing = await carts.FindActiveByCustomerAsync(command.CustomerId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Currency, money.Value.Currency, StringComparison.Ordinal))
            {
                return Result<OpenedCart>.Failure(CartErrors.AlreadyOpen(existing.Currency));
            }

            return Result<OpenedCart>.Success(new OpenedCart(await ViewOfAsync(existing, cancellationToken), false));
        }

        var cart = ShoppingCart.Open(command.CustomerId, money.Value.Currency, timeProvider);
        if (cart.IsFailure)
        {
            return Result<OpenedCart>.Failure(cart.Error);
        }

        await carts.AddAsync(cart.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OpenedCart>.Success(new OpenedCart(await ViewOfAsync(cart.Value, cancellationToken), true));
    }

    private async Task<CartView> ViewOfAsync(ShoppingCart cart, CancellationToken cancellationToken) =>
        cart.ToView(await products.GetManyAsync([.. cart.Items.Select(item => item.ProductId)], cancellationToken));
}
