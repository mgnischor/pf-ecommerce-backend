namespace Portfolio.Ordering.Application;

/// <summary>A line to place, as the checkout priced it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">SKU at purchase time.</param>
/// <param name="Name">Product name at purchase time.</param>
/// <param name="Quantity">Units purchased.</param>
/// <param name="UnitPrice">Unit price amount at purchase time.</param>
/// <param name="Currency">ISO 4217 currency of the unit price.</param>
internal sealed record PlaceOrderLine(
    Guid ProductId,
    string? Sku,
    string? Name,
    int Quantity,
    decimal UnitPrice,
    string? Currency
);
