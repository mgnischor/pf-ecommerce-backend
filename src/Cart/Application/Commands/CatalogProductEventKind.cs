namespace Portfolio.Cart.Application;

/// <summary>The Catalog integration events the Cart keeps its view of the catalog from (BR-CRT-005).</summary>
internal enum CatalogProductEventKind
{
    /// <summary><c>catalog.product-created</c>.</summary>
    Created = 0,

    /// <summary><c>catalog.product-price-changed</c>.</summary>
    PriceChanged = 1,

    /// <summary><c>catalog.product-status-changed</c>.</summary>
    StatusChanged = 2,

    /// <summary><c>catalog.product-deleted</c>.</summary>
    Deleted = 3,
}
