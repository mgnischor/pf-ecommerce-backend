namespace Portfolio.Cart.Application;

/// <summary>The outcome of opening a cart: the cart, and whether this request created it.</summary>
/// <param name="Cart">The customer's active cart.</param>
/// <param name="Created"><c>true</c> when the cart was created now; <c>false</c> when the customer already had it open.</param>
internal sealed record OpenedCart(CartView Cart, bool Created);
