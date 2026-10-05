namespace Portfolio.Ordering.Application;

/// <summary>Read model of one line of an order, priced as at purchase time.</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Sku">SKU at purchase time.</param>
/// <param name="Name">Product name at purchase time.</param>
/// <param name="Quantity">Units purchased.</param>
/// <param name="UnitPriceAmount">Unit price amount at purchase time.</param>
/// <param name="LineTotalAmount">Unit price multiplied by quantity.</param>
/// <param name="Currency">ISO 4217 currency of the prices.</param>
internal sealed record OrderItemView(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    string Currency
);
