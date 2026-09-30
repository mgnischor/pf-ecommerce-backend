namespace Portfolio.SharedKernel.API;

/// <summary>
/// Business roles used in authorization attributes (ai/BUSINESS.md §8.1). Composite constants list
/// the roles allowed to perform a class of actions; anything not listed is denied by default.
/// </summary>
public static class ApiRoles
{
    /// <summary>A shopper acting on their own resources.</summary>
    public const string Customer = "Customer";

    /// <summary>Manages the product catalog and stock.</summary>
    public const string CatalogManager = "CatalogManager";

    /// <summary>Assists customers with orders, shipments, and refunds.</summary>
    public const string SupportAgent = "SupportAgent";

    /// <summary>Reviews payments and approves refunds.</summary>
    public const string FinanceAnalyst = "FinanceAnalyst";

    /// <summary>Full back-office access.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Roles that maintain the catalog and inventory.</summary>
    public const string CatalogStaff = CatalogManager + "," + Administrator;

    /// <summary>Roles that act on any customer's orders and shipments.</summary>
    public const string OrderStaff = SupportAgent + "," + Administrator;

    /// <summary>Roles that may refund payments.</summary>
    public const string FinanceStaff = FinanceAnalyst + "," + Administrator;

    /// <summary>Customers and the staff allowed to see their orders (ownership is enforced per resource).</summary>
    public const string CustomerOrStaff = Customer + "," + SupportAgent + "," + Administrator;
}
