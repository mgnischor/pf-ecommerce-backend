using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised on every product lifecycle transition (BR-CAT-003).
/// </summary>
public sealed record ProductStatusChanged(
    Guid EventId,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    ProductStatus From,
    ProductStatus To) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a status transition.
    /// </summary>
    /// <param name="product">The product that transitioned.</param>
    /// <param name="from">Status before the transition.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static ProductStatusChanged For(Product product, ProductStatus from, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ProductStatusChanged(
            Guid.CreateVersion7(),
            product.Id,
            timeProvider.GetUtcNow(),
            from,
            product.Status);
    }
}
