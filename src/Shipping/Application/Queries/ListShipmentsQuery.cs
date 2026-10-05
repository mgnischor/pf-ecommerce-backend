namespace Portfolio.Shipping.Application;

/// <summary>Request to list the shipments of an order (BR-SHP-004).</summary>
/// <param name="CustomerId">The authenticated account; an order of another customer does not exist for them.</param>
/// <param name="OrderId">The order.</param>
/// <param name="Limit">Page size; clamped to 1–100.</param>
/// <param name="Cursor">Signed cursor of the previous page, or <c>null</c> for the first page.</param>
internal sealed record ListShipmentsQuery(Guid CustomerId, Guid OrderId, int Limit, string? Cursor);
