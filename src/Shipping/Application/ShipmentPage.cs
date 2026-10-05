namespace Portfolio.Shipping.Application;

/// <summary>One page of the shipments of an order.</summary>
/// <param name="Items">Shipments of the page; empty, never <c>null</c>.</param>
/// <param name="NextCursor">Signed cursor of the next page, or <c>null</c> on the last page.</param>
/// <param name="HasMore">Whether another page exists.</param>
internal sealed record ShipmentPage(IReadOnlyList<ShipmentView> Items, string? NextCursor, bool HasMore);
