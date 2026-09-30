using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised when a product price changes (BR-CAT-002).
/// </summary>
public sealed record ProductPriceChanged(
    Guid EventId,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    Money OldPrice,
    Money NewPrice) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a price change.
    /// </summary>
    /// <param name="product">The product whose price changed.</param>
    /// <param name="oldPrice">Price before the change.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static ProductPriceChanged For(Product product, Money oldPrice, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(oldPrice);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ProductPriceChanged(
            Guid.CreateVersion7(),
            product.Id,
            timeProvider.GetUtcNow(),
            oldPrice,
            product.Price);
    }
}
