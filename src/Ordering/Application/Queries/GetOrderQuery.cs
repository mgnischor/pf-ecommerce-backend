namespace Portfolio.Ordering.Application;

/// <summary>Request to read one order (BR-ORD-006).</summary>
/// <param name="CustomerId">The authenticated account; an order of another customer does not exist for them.</param>
/// <param name="OrderId">Order identifier.</param>
internal sealed record GetOrderQuery(Guid CustomerId, Guid OrderId);
