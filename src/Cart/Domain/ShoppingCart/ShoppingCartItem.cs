using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// One line of a <see cref="ShoppingCart"/>: a product and how many units of it (BR-CRT-002, BR-CRT-003). The line
/// stores no price: prices are read from the catalog view at the time they are shown (BR-CRT-005).
/// </summary>
internal sealed class ShoppingCartItem : Entity
{
    /// <summary>The cart the line belongs to.</summary>
    public Guid CartId { get; private set; }

    /// <summary>The product, by identifier only (ai/ARCHITECTURE.md §4.1).</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Units in the line, 1 to <see cref="ShoppingCart.MaxLineQuantity"/>.</summary>
    public int Quantity { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    private ShoppingCartItem() { }

    private ShoppingCartItem(Guid id, Guid cartId, Guid productId, int quantity, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
    }

    /// <summary>Creates a line. The cart validates the quantity before calling.</summary>
    /// <param name="cartId">Owning cart.</param>
    /// <param name="productId">Product.</param>
    /// <param name="quantity">Units.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    internal static ShoppingCartItem Create(Guid cartId, Guid productId, int quantity, TimeProvider timeProvider) =>
        new(NewId(timeProvider), cartId, productId, quantity, timeProvider);

    /// <summary>Adds units to the line. The cart validates the new total before calling.</summary>
    /// <param name="quantity">Units to add.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    internal void Increase(int quantity, TimeProvider timeProvider)
    {
        Quantity += quantity;
        MarkUpdated(timeProvider);
    }
}
