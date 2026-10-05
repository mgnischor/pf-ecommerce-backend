using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>One line of an <see cref="OrderPlaced"/> event: what a consumer needs to act on the order without querying back.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">SKU at purchase time.</param>
/// <param name="Quantity">Units purchased.</param>
/// <param name="UnitPrice">Unit price at purchase time.</param>
internal sealed record OrderPlacedItem(Guid ProductId, string Sku, int Quantity, Money UnitPrice);
