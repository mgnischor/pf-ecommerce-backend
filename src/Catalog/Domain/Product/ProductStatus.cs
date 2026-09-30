namespace Portfolio.Catalog.Domain;

/// <summary>
/// Lifecycle status of a <see cref="Product"/> (BR-CAT-003).
/// Valid transitions: Draft → Active → Discontinued.
/// </summary>
public enum ProductStatus
{
    /// <summary>Created but not yet sellable.</summary>
    Draft = 0,

    /// <summary>Visible and sellable in the catalog.</summary>
    Active = 1,

    /// <summary>Permanently withdrawn from the catalog.</summary>
    Discontinued = 2,
}
