using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised when a product price changes (BR-CAT-002).
/// </summary>
internal sealed record ProductPriceChanged(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    Money OldPrice,
    Money NewPrice
) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a price change. Must be called after the state change.
    /// </summary>
    /// <param name="product">The product whose price changed.</param>
    /// <param name="oldPrice">Price before the change.</param>
    public static ProductPriceChanged For(Product product, Money oldPrice)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(oldPrice);

        return new ProductPriceChanged(
            Guid.CreateVersion7(product.UpdatedAt),
            product.Id,
            product.Version,
            product.UpdatedAt,
            oldPrice,
            product.Price
        );
    }
}
