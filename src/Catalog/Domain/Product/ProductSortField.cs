namespace Portfolio.Catalog.Domain;

/// <summary>Field a product listing can be ordered by (BR-CAT-006).</summary>
internal enum ProductSortField
{
    /// <summary>Creation instant; the default, newest first.</summary>
    CreatedAt = 0,

    /// <summary>Display name, in the database collation.</summary>
    Name = 1,

    /// <summary>Sell price amount.</summary>
    Price = 2,
}
