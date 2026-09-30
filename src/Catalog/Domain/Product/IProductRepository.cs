using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="Product"/> aggregate.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
public interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// Finds an active product by SKU, or <c>null</c> when not found.
    /// </summary>
    /// <param name="sku">Stock-keeping unit to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default);
}
