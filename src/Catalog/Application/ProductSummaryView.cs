namespace Portfolio.Catalog.Application;

/// <summary>Read model of a product in a collection: what the API may show, without exposing the aggregate.</summary>
/// <param name="Id">Product identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Sku">Normalized stock-keeping unit.</param>
/// <param name="PriceAmount">Sell price amount.</param>
/// <param name="PriceCurrency">ISO 4217 currency code of the price.</param>
/// <param name="Status">Lifecycle status.</param>
internal sealed record ProductSummaryView(
    Guid Id,
    string Name,
    string Sku,
    decimal PriceAmount,
    string PriceCurrency,
    ProductStatusView Status
);
