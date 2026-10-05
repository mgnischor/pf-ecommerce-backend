using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// One line of an <see cref="Order"/>, priced as at purchase time (BR-ORD-002): the product, its SKU and name, the
/// quantity, and the unit price the customer was charged. Immutable once the order is placed, so a later change of the
/// catalog never changes an order that was already placed.
/// </summary>
internal sealed class OrderItem : Entity
{
    /// <summary>Maximum length of a snapshotted product name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Maximum length of a snapshotted SKU.</summary>
    public const int MaxSkuLength = 32;

    /// <summary>The order the line belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The product, by identifier only (ai/ARCHITECTURE.md §4.1).</summary>
    public Guid ProductId { get; private set; }

    /// <summary>SKU at purchase time.</summary>
    public string Sku { get; private set; }

    /// <summary>Product name at purchase time.</summary>
    public string Name { get; private set; }

    /// <summary>Units purchased.</summary>
    public int Quantity { get; private set; }

    /// <summary>Unit price at purchase time.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Unit price multiplied by quantity (BR-ORD-002).</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private OrderItem() { }
#pragma warning restore CS8618

    private OrderItem(
        Guid id,
        Guid orderId,
        Guid productId,
        string sku,
        string name,
        int quantity,
        Money unitPrice,
        TimeProvider timeProvider
    )
        : base(id, timeProvider)
    {
        OrderId = orderId;
        ProductId = productId;
        Sku = sku;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>Creates a line from an already validated draft.</summary>
    /// <param name="orderId">Owning order.</param>
    /// <param name="draft">The validated line.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    internal static OrderItem From(Guid orderId, OrderLine draft, TimeProvider timeProvider) =>
        new(
            NewId(timeProvider),
            orderId,
            draft.ProductId,
            draft.Sku.Trim(),
            draft.Name.Trim(),
            draft.Quantity,
            draft.UnitPrice,
            timeProvider
        );
}
