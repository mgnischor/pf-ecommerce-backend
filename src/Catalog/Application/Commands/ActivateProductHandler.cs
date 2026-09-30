using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Activates a product (BR-CAT-003). A repeated request is rejected by the state machine as an
/// invalid transition, never applied twice; concurrent activations are arbitrated by the version token.
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
    public async Task<Result> HandleAsync(ActivateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure(ProductErrors.NotFound);
        }

        var result = product.Activate(timeProvider);
        if (result.IsFailure)
        {
            return result;
        }

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
