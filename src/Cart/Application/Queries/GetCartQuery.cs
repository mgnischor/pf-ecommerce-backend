namespace Portfolio.Cart.Application;

/// <summary>Request to read a cart.</summary>
/// <param name="CustomerId">The authenticated account; a cart of another customer does not exist for them.</param>
/// <param name="CartId">Cart identifier.</param>
internal sealed record GetCartQuery(Guid CustomerId, Guid CartId);
