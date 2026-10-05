using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Reaction to one of the Catalog's product integration events: update the Cart's own view of that product.
/// The Cart's own reading of the event; it never references the Catalog's types.
/// </summary>
/// <param name="MessageId">Identifier of the event, which the inbox deduplicates on.</param>
/// <param name="Kind">Which event it is.</param>
/// <param name="ProductId">The product (the event's aggregate identifier).</param>
/// <param name="SourceVersion">Catalog version of the product after the change (the event's aggregate version).</param>
/// <param name="Sku">SKU; set by <see cref="CatalogProductEventKind.Created"/>.</param>
/// <param name="Name">Name; set by <see cref="CatalogProductEventKind.Created"/>.</param>
/// <param name="Price">Price; the initial one for <see cref="CatalogProductEventKind.Created"/>, the new one for <see cref="CatalogProductEventKind.PriceChanged"/>.</param>
/// <param name="Status">Status the product reached; set by <see cref="CatalogProductEventKind.StatusChanged"/> (<c>draft</c>, <c>active</c>, <c>discontinued</c>).</param>
internal sealed record SyncCatalogProductCommand(
    Guid MessageId,
    CatalogProductEventKind Kind,
    Guid ProductId,
    int SourceVersion,
    string? Sku,
    string? Name,
    Money? Price,
    string? Status
);
