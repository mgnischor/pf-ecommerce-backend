namespace Portfolio.Cart.Application;

/// <summary>Request to add units of a product to a cart (BR-CRT-002, BR-CRT-003, BR-CRT-005).</summary>
/// <param name="CustomerId">The authenticated account; only the cart's owner can change it.</param>
/// <param name="CartId">Cart identifier.</param>
/// <param name="ProductId">Product to add.</param>
/// <param name="Quantity">Units to add.</param>
internal sealed record AddCartItemCommand(Guid CustomerId, Guid CartId, Guid ProductId, int Quantity);
