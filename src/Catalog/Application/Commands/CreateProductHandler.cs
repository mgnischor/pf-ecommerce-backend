using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Creates a product. Idempotent: the product stores the client's <c>Idempotency-Key</c> (BR-CAT-008), so a retry of
/// the same request answers with the product it created and never creates a second one, while the same key with a
/// different request is rejected. Without a key, the SKU uniqueness check (BR-CAT-005) still turns a replay into a
/// conflict, and the partial unique index on <c>sku</c> closes the race between two concurrent requests.
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
    /// <returns>The created product, or the violated rule.</returns>
    public async Task<Result<ProductView>> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var sku = Sku.Create(command.Sku);
        if (sku.IsFailure)
        {
            return Result<ProductView>.Failure(sku.Error);
        }

        var price = Money.Create(command.Price, command.Currency);
        if (price.IsFailure)
        {
            return Result<ProductView>.Failure(price.Error);
        }

        if (command.IdempotencyKey is not null)
        {
            var previous = await products.FindByCreationKeyAsync(command.IdempotencyKey, cancellationToken);
            if (previous is not null)
            {
                return
                    !previous.IsDeleted
                    && previous.WasCreatedFrom(sku.Value, command.Name, price.Value, command.Description)
                    ? Result<ProductView>.Success(previous.ToView())
                    : Result<ProductView>.Failure(ProductErrors.IdempotencyKeyReused);
            }
        }

        if (await products.FindBySkuAsync(sku.Value, cancellationToken) is not null)
        {
            return Result<ProductView>.Failure(ProductErrors.SkuAlreadyExists);
        }

        var product = Product.Create(
            command.Name,
            sku.Value,
            price.Value,
            command.Description,
            timeProvider,
            command.IdempotencyKey
        );
        if (product.IsFailure)
        {
            return Result<ProductView>.Failure(product.Error);
        }

        await products.AddAsync(product.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ProductView>.Success(product.Value.ToView());
    }
}
