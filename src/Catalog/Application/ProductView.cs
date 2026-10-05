namespace Portfolio.Catalog.Application;

/// <summary>Read model of one product.</summary>
/// <param name="Id">Product identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Description, or <c>null</c> when absent.</param>
/// <param name="Sku">Normalized stock-keeping unit.</param>
/// <param name="PriceAmount">Sell price amount.</param>
/// <param name="PriceCurrency">ISO 4217 currency code of the price.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Version">Aggregate version, the source of the <c>ETag</c>.</param>
/// <param name="AllowedActions">Actions the lifecycle currently allows (see <see cref="ProductActions"/>).</param>
/// <param name="CreatedAt">UTC creation instant.</param>
/// <param name="UpdatedAt">UTC instant of the last change.</param>
internal sealed record ProductView(
    Guid Id,
    string Name,
    string? Description,
    string Sku,
    decimal PriceAmount,
    string PriceCurrency,
    ProductStatusView Status,
    int Version,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
