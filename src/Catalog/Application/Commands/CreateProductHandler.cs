using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Creates a product. Retrying the same request is safe: the SKU uniqueness check (BR-CAT-005) turns a
/// replay into a conflict instead of a duplicate, and the partial unique index on <c>sku</c> closes the
/// race between two concurrent requests. The HTTP <c>Idempotency-Key</c> replays the original response.
/// </summary>
internal sealed class CreateProductHandler(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the new product, or the violated rule.</returns>
    public async Task<Result<Guid>> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
        {
            return Result<Guid>.Failure(sku.Error);
        }

        var price = Money.Create(command.Price, command.Currency);
        if (price.IsFailure)
        {
            return Result<Guid>.Failure(price.Error);
        }

        if (await products.FindBySkuAsync(sku.Value, cancellationToken) is not null)
        {
            return Result<Guid>.Failure(ProductErrors.SkuAlreadyExists);
        }

        var product = Product.Create(command.Name, sku.Value, price.Value, command.Description, timeProvider);
        if (product.IsFailure)
        {
            return Result<Guid>.Failure(product.Error);
        }

        await products.AddAsync(product.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(product.Value.Id);
    }
}
