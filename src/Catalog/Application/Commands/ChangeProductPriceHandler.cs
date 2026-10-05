using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Changes a product price (BR-CAT-002). Idempotent: repeating the command with the price the product already has
/// succeeds without raising another <see cref="ProductPriceChanged"/> event, whatever version the retry carries,
/// because replacing a value with itself changes nothing a stale client could overwrite.
/// </summary>
internal sealed class ChangeProductPriceHandler(
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
        ChangeProductPriceCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var price = Money.Create(command.Price, command.Currency);
        if (price.IsFailure)
        {
            return Result<ProductView>.Failure(price.Error);
        }

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result<ProductView>.Failure(ProductErrors.NotFound);
        }

        if (product.Price.Equals(price.Value))
        {
            return Result<ProductView>.Success(product.ToView());
        }

        if (command.ExpectedVersion != product.Version)
        {
            return Result<ProductView>.Failure(ProductErrors.VersionMismatch);
        }

        var result = product.ChangePrice(price.Value, timeProvider);
        if (result.IsFailure)
        {
            return Result<ProductView>.Failure(result.Error);
        }

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ProductView>.Success(product.ToView());
    }
}
