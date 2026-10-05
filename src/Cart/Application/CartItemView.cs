namespace Portfolio.Cart.Application;

/// <summary>Read model of one line of a cart, priced from the catalog view at the time it is built.</summary>
/// <param name="Id">Line identifier.</param>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Name">Product name.</param>
/// <param name="Quantity">Units in the line.</param>
/// <param name="UnitPriceAmount">Current unit price amount.</param>
/// <param name="LineTotalAmount">Unit price multiplied by quantity.</param>
/// <param name="Currency">ISO 4217 currency of the line's prices (the product's currency).</param>
internal sealed record CartItemView(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    string Currency
);
