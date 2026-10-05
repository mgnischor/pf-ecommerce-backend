namespace Portfolio.Catalog.Application;

/// <summary>Names of the actions a product's lifecycle can allow, as exposed in <c>allowedActions</c>.</summary>
internal static class ProductActions
{
    /// <summary>Rename or re-describe the product.</summary>
    public const string Update = "update";

    /// <summary>Replace the sell price.</summary>
    public const string ChangePrice = "change-price";

    /// <summary>Make a draft product sellable.</summary>
    public const string Activate = "activate";

    /// <summary>Permanently withdraw an active product.</summary>
    public const string Discontinue = "discontinue";

    /// <summary>Logically delete the product.</summary>
    public const string Delete = "delete";
}
