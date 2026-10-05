using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Reads one product (BR-CAT-006). A product the caller may not see answers exactly like one that does not exist,
/// so a draft or discontinued product is never revealed to the public.
/// </summary>
internal sealed class GetProductHandler(IProductRepository products)
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The product, or the reason it cannot be read.</returns>
    public async Task<Result<ProductView>> HandleAsync(GetProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await products.GetByIdAsync(query.ProductId, cancellationToken);

        return product is null || (!query.CanSeeAllStatuses && product.Status != ProductStatus.Active)
            ? Result<ProductView>.Failure(ProductErrors.NotFound)
            : Result<ProductView>.Success(product.ToView());
    }
}
