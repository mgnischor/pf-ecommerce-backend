using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Application;

/// <summary>Projects the aggregate, with the catalog view that prices it, onto its read model.</summary>
internal static class CartMapping
{
    /// <summary>
    /// Builds the view of <paramref name="cart"/>. A line whose product is priced in another currency than the cart
    /// (the catalog changed it after the product was added) is shown with its own currency but left out of the
    /// subtotal, which only ever adds amounts of the cart's currency (BR-CRT-005). A line whose product the cart cannot
    /// resolve is left out of the view: lines are only created for products the cart knows, and the view is never
    /// deleted, so this cannot happen in a consistent database.
    /// </summary>
    /// <param name="cart">The cart.</param>
    /// <param name="products">The catalog view of the products of its lines.</param>
    public static CartView ToView(this ShoppingCart cart, IReadOnlyDictionary<Guid, CatalogProduct> products)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(products);

        var items = cart
            .Items.Where(item => products.ContainsKey(item.ProductId))
            .Select(item => LineOf(item, products[item.ProductId]))
            .ToList();

        var subtotal = items
            .Where(line => string.Equals(line.Currency, cart.Currency, StringComparison.Ordinal))
            .Sum(line => line.LineTotalAmount);

        return new CartView(
            cart.Id,
            cart.Status.ToView(),
            cart.Currency,
            items,
            subtotal,
            cart.Version,
            AllowedActionsOf(cart)
        );
    }

    /// <summary>Maps a domain status to the status of the read model.</summary>
    /// <param name="status">Domain status.</param>
    public static CartStatusView ToView(this CartStatus status) =>
        status switch
        {
            CartStatus.Active => CartStatusView.Active,
            CartStatus.CheckedOut => CartStatusView.CheckedOut,
            CartStatus.Expired => CartStatusView.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown cart status."),
        };

    private static CartItemView LineOf(ShoppingCartItem item, CatalogProduct product) =>
        new(
            item.Id,
            item.ProductId,
            product.Sku,
            product.Name,
            item.Quantity,
            product.Price.Amount,
            product.Price.Multiply(item.Quantity).Amount,
            product.Price.Currency
        );

    private static string[] AllowedActionsOf(ShoppingCart cart)
    {
        if (cart.Status != CartStatus.Active)
        {
            return [];
        }

        return cart.Items.Count == 0
            ? [CartActions.AddItem]
            : [CartActions.AddItem, CartActions.RemoveItem, CartActions.Checkout];
    }
}
