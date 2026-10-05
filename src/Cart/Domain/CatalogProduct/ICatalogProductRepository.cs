using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// Persistence abstraction for the Cart's view of the catalog. Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface ICatalogProductRepository : IRepository<CatalogProduct>
{
    /// <summary>Loads the views of several products at once; products the cart does not know are simply absent.</summary>
    /// <param name="productIds">Catalog product identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default
    );
}
