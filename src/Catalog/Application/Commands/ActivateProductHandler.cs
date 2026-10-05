using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Activates a product (BR-CAT-003). A repeated request is rejected, never applied twice: with the stale version it
/// carried, as a failed precondition; with the current one, by the state machine as an invalid transition.
/// Concurrent activations are arbitrated by the version token.
/// </summary>
internal sealed class ActivateProductHandler(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The product after the change, or the violated rule.</returns>
    public async Task<Result<ProductView>> HandleAsync(
        ActivateProductCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<ProductView>.Failure(ProductErrors.NotFound);
        }

        if (command.ExpectedVersion != product.Version)
        {
            return Result<ProductView>.Failure(ProductErrors.VersionMismatch);
        }

        var result = product.Activate(timeProvider);
        if (result.IsFailure)
        {
            return Result<ProductView>.Failure(result.Error);
        }

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ProductView>.Success(product.ToView());
    }
}
