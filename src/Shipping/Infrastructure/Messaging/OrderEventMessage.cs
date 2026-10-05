namespace Portfolio.Shipping.Infrastructure;

/// <summary>
/// Shipping's reading of the Ordering's order events: a superset of the fields of <c>order-placed</c>, <c>order-paid</c>
/// and <c>order-cancelled</c>, of which each event fills what it carries. Other fields in the payload are ignored.
/// </summary>
/// <param name="AggregateId">The order.</param>
/// <param name="CustomerId">The order's owner.</param>
/// <param name="Number">The order number.</param>
internal sealed record OrderEventMessage(Guid AggregateId, Guid CustomerId, string? Number);
