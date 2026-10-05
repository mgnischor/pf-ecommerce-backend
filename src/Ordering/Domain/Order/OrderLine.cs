using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>A line of an order to place, as the checkout priced it: validated by <see cref="Order.Place"/> (BR-ORD-001).</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">SKU at purchase time.</param>
/// <param name="Name">Product name at purchase time.</param>
/// <param name="Quantity">Units purchased.</param>
/// <param name="UnitPrice">Unit price at purchase time.</param>
internal sealed record OrderLine(Guid ProductId, string Sku, string Name, int Quantity, Money UnitPrice);
