namespace Portfolio.Cart.Application;

/// <summary>Read model of a cart: what the API may show, without exposing the aggregate.</summary>
/// <param name="Id">Cart identifier.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Currency">ISO 4217 currency the cart is priced in.</param>
/// <param name="Items">Lines of the cart.</param>
/// <param name="SubtotalAmount">Sum of the line totals priced in the cart's currency (BR-CRT-005).</param>
/// <param name="Version">Aggregate version, the source of the <c>ETag</c>.</param>
/// <param name="AllowedActions">Actions the lifecycle currently allows (see <see cref="CartActions"/>).</param>
internal sealed record CartView(
    Guid Id,
    CartStatusView Status,
    string Currency,
    IReadOnlyList<CartItemView> Items,
    decimal SubtotalAmount,
    int Version,
    IReadOnlyList<string> AllowedActions
);
