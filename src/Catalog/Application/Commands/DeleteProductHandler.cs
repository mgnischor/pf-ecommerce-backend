using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Logically deletes a product and releases its SKU (BR-CAT-007). Idempotent: the product keeps the client's
/// <c>Idempotency-Key</c> (BR-CAT-008), so a retry after the deletion succeeded answers as that success instead of
/// "not found". The replay check runs before the version check because a retry necessarily carries the version the
/// original request read, which the original already advanced.
/// </summary>
internal sealed class DeleteProductHandler(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return await products.WasDeletedByAsync(command.ProductId, command.IdempotencyKey, cancellationToken)
                ? Result.Success()
                : Result.Failure(ProductErrors.NotFound);
        }

        if (command.ExpectedVersion != product.Version)
        {
            return Result.Failure(ProductErrors.VersionMismatch);
        }

        product.Delete(command.IdempotencyKey, timeProvider);
        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
