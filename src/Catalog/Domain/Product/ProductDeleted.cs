using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised when a product is logically deleted (BR-CAT-007). Consumers that keep their own view of the
/// catalog use it to stop treating the product as sellable.
/// </summary>
internal sealed record ProductDeleted(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Sku
) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a deletion. Must be called after the state change.
    /// </summary>
    /// <param name="product">The deleted product.</param>
    public static ProductDeleted For(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductDeleted(
            Guid.CreateVersion7(product.UpdatedAt),
            product.Id,
            product.Version,
            product.UpdatedAt,
            product.Sku.Value
        );
    }
}
