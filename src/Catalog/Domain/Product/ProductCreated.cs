using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised when a product is created.
/// </summary>
public sealed record ProductCreated(
    Guid EventId,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    string Name,
    string Sku) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a newly created product.
    /// </summary>
    /// <param name="product">The created product.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static ProductCreated For(Product product, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ProductCreated(
            Guid.CreateVersion7(),
            product.Id,
            timeProvider.GetUtcNow(),
            product.Name,
            product.Sku.Value);
    }
}
