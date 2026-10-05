namespace Portfolio.Cart.Application;

/// <summary>Request to remove a line from a cart.</summary>
/// <param name="CustomerId">The authenticated account; only the cart's owner can change it.</param>
/// <param name="CartId">Cart identifier.</param>
/// <param name="ItemId">Line identifier.</param>
internal sealed record RemoveCartItemCommand(Guid CustomerId, Guid CartId, Guid ItemId);
