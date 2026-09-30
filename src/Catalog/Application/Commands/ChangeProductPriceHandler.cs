using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>
/// Changes a product price (BR-CAT-002). Idempotent: repeating the command with the same price
/// succeeds without raising another <see cref="ProductPriceChanged"/> event.
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
    public async Task<Result> HandleAsync(ChangeProductPriceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var price = Money.Create(command.Price, command.Currency);
        if (price.IsFailure)
        {
            return Result.Failure(price.Error);
        }

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure(ProductErrors.NotFound);
        }

        var result = product.ChangePrice(price.Value, timeProvider);
        if (result.IsFailure)
        {
            return result;
        }

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
