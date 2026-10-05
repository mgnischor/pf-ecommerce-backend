namespace Portfolio.Ordering.Application;

/// <summary>Request to place the order of a checkout (BR-ORD-001, BR-ORD-002). The total is computed, never given.</summary>
/// <param name="CheckoutId">The checkout; it can produce at most one order.</param>
/// <param name="CustomerId">The customer the order belongs to.</param>
/// <param name="Lines">The lines, priced as at purchase time.</param>
internal sealed record PlaceOrderCommand(Guid CheckoutId, Guid CustomerId, IReadOnlyList<PlaceOrderLine>? Lines);
