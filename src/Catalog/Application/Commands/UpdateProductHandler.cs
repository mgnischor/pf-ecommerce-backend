using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Renames a product or changes its description (BR-CAT-001). Idempotent: a retry that asks for what the product
/// already holds succeeds without advancing the version, while a stale <c>If-Match</c> is a failed precondition.
/// </summary>
internal sealed class UpdateProductHandler(
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
        UpdateProductCommand command,
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

        var result = product.Update(
            command.ChangeName,
            command.Name,
            command.ChangeDescription,
            command.Description,
            timeProvider
        );
        if (result.IsFailure)
        {
            return Result<ProductView>.Failure(result.Error);
        }

        if (product.Version != command.ExpectedVersion)
        {
            products.Update(product);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<ProductView>.Success(product.ToView());
    }
}
