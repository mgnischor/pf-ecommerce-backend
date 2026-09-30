using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised on every product lifecycle transition (BR-CAT-003).
/// </summary>
internal sealed record ProductStatusChanged(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    ProductStatus From,
    ProductStatus To
) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a status transition. Must be called after the state change.
    /// </summary>
    /// <param name="product">The product that transitioned.</param>
    /// <param name="from">Status before the transition.</param>
    public static ProductStatusChanged For(Product product, ProductStatus from)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductStatusChanged(
            Guid.CreateVersion7(product.UpdatedAt),
            product.Id,
            product.Version,
            product.UpdatedAt,
            from,
            product.Status
        );
    }
}
