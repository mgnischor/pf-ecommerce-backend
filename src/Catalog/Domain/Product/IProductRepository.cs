using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="Product"/> aggregate.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// Finds an active (not deleted) product by SKU, or <c>null</c> when not found.
    /// </summary>
    /// <param name="sku">Stock-keeping unit to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the product a client already created with <paramref name="idempotencyKey"/> (BR-CAT-008), even when it
    /// was deleted since, or <c>null</c> when the key was never used.
    /// </summary>
    /// <param name="idempotencyKey">Key sent by the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Product?> FindByCreationKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the product was logically deleted by the request that carried <paramref name="idempotencyKey"/>
    /// (BR-CAT-008), so a retry of that request can be answered as the success it was.
    /// </summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="idempotencyKey">Key sent by the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> WasDeletedByAsync(Guid productId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active (not deleted) products in the order and from the position of <paramref name="criteria"/>
    /// (BR-CAT-006). Read-only: the products are not tracked.
    /// </summary>
    /// <param name="criteria">Ordering, filters, page size, and position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Product>> ListAsync(ProductListCriteria criteria, CancellationToken cancellationToken = default);
}
