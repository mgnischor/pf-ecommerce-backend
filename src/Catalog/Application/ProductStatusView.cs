namespace Portfolio.Catalog.Application;

/// <summary>Lifecycle status of a product as the API may show it (BR-CAT-003).</summary>
internal enum ProductStatusView
{
    /// <summary>Created but not yet sellable.</summary>
    Draft = 0,

    /// <summary>Visible and sellable.</summary>
    Active = 1,

    /// <summary>Permanently withdrawn.</summary>
    Discontinued = 2,
}
