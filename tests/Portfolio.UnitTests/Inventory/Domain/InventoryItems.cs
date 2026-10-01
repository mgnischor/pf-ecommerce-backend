using Portfolio.Inventory.Domain;

namespace Portfolio.UnitTests.Inventory.Domain;

/// <summary>
/// Builds valid inventory items with sensible defaults; tests override only what they assert on (ai/TESTS.md §10).
/// The returned item has no pending domain events, so assertions see only what the test provokes.
/// </summary>
internal static class InventoryItems
{
    public static readonly Guid Actor = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f09");

    public static Sku SkuOf(string value = "CAF-600-PRT") => Sku.Create(value).Value;

    /// <summary>An item holding <paramref name="onHand"/> units, <paramref name="reserved"/> of them reserved.</summary>
    public static InventoryItem With(TimeProvider clock, int onHand = 0, int reserved = 0, string sku = "CAF-600-PRT")
    {
        var item = InventoryItem.Open(SkuOf(sku), clock);

        if (onHand > 0)
        {
            item.Adjust(onHand, "stocktake", Actor, null, clock).IsSuccess.ShouldBeTrue();
        }

        if (reserved > 0)
        {
            item.Reserve(reserved, clock).IsSuccess.ShouldBeTrue();
        }

        item.ClearDomainEvents();
        return item;
    }
}
