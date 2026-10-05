namespace Portfolio.Cart.Application;

/// <summary>Names of the actions a cart's lifecycle can allow, as exposed in <c>allowedActions</c>.</summary>
internal static class CartActions
{
    /// <summary>Add a product, or more units of one.</summary>
    public const string AddItem = "add-item";

    /// <summary>Remove a line.</summary>
    public const string RemoveItem = "remove-item";

    /// <summary>Convert the cart into a checkout; only a cart with at least one line.</summary>
    public const string Checkout = "checkout";
}
