using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Event raised when a product is created. Carries the full initial snapshot so consumers
/// (cart, search, inventory) never need to query back.
/// </summary>
internal sealed record ProductCreated(
    Guid EventId,
    Guid AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string Name,
    string Sku,
    Money Price
) : IDomainEvent
{
    /// <summary>
    /// Creates the event for a newly created product. Must be called after the state change.
    /// </summary>
    /// <param name="product">The created product.</param>
    public static ProductCreated For(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductCreated(
            Guid.CreateVersion7(product.CreatedAt),
            product.Id,
            product.Version,
            product.CreatedAt,
            product.Name,
            product.Sku.Value,
            product.Price
        );
    }
}
