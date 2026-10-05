namespace Portfolio.Ordering.Application;

/// <summary>One page of an order listing.</summary>
/// <param name="Items">Orders of the page; empty, never <c>null</c>.</param>
/// <param name="NextCursor">Signed cursor of the next page, or <c>null</c> on the last page.</param>
/// <param name="HasMore">Whether another page exists.</param>
internal sealed record OrderPage(IReadOnlyList<OrderSummaryView> Items, string? NextCursor, bool HasMore);
