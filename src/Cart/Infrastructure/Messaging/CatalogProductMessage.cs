using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>
/// The Cart's reading of the Catalog's product events: a superset of the fields of <c>product-created</c>,
/// <c>product-price-changed</c>, <c>product-status-changed</c> and <c>product-deleted</c>, of which each event fills its own.
/// Other fields in the payload are ignored.
/// </summary>
/// <param name="AggregateId">The product.</param>
/// <param name="AggregateVersion">Catalog version of the product after the change.</param>
/// <param name="Sku"><c>product-created</c>: the SKU.</param>
/// <param name="Name"><c>product-created</c>: the name.</param>
/// <param name="Price"><c>product-created</c>: the initial price.</param>
/// <param name="NewPrice"><c>product-price-changed</c>: the new price.</param>
/// <param name="To"><c>product-status-changed</c>: the status the product reached.</param>
internal sealed record CatalogProductMessage(
    Guid AggregateId,
    int AggregateVersion,
    string? Sku,
    string? Name,
    Money? Price,
    Money? NewPrice,
    string? To
);
