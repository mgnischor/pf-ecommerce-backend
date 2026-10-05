using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Application;

/// <summary>
/// Keeps the Cart's view of the catalog up to date from the Catalog's events (BR-CRT-005). Delivery is at least once
/// and not ordered, so the handler is idempotent three times over: the inbox row of the message is committed with the
/// change (a redelivery finds it and does nothing), an event already created is not created again, and every change
/// carries the Catalog version that produced it, so an older event never overwrites a newer one.
/// A price, status or deletion event for a product whose creation has not arrived yet fails with
/// <see cref="UnknownProduct"/>, which the consumer retries until the creation is processed.
/// </summary>
internal sealed class SyncCatalogProductHandler(
    ICatalogProductRepository products,
    IUnitOfWork unitOfWork,
    IInbox inbox,
    TimeProvider timeProvider
)
{
    /// <summary>Stable consumer name: the inbox key, the queue name and the telemetry label.</summary>
    public const string ConsumerName = "cart.sync-catalog-products";

    /// <summary>The event refers to a product whose creation the Cart has not processed yet. Transient: retry.</summary>
    public static Error UnknownProduct => Error.NotFound("CART_CATALOG_PRODUCT_UNKNOWN");

    /// <summary>The event lacks data its kind requires, so no retry can ever make it valid.</summary>
    public static Error MalformedEvent => Error.Validation("CART_CATALOG_EVENT_MALFORMED");

    /// <summary>Executes the command.</summary>
    /// <param name="command">The event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the message was handled now, <c>false</c> when it was a duplicate, or why it cannot be handled.</returns>
    public async Task<Result<bool>> HandleAsync(SyncCatalogProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsWellFormed(command))
        {
            return Result<bool>.Failure(MalformedEvent);
        }

        if (!await inbox.TryBeginAsync(command.MessageId, ConsumerName, cancellationToken))
        {
            return Result<bool>.Success(false);
        }

        var product = await products.GetByIdAsync(command.ProductId, cancellationToken);
        if (command.Kind == CatalogProductEventKind.Created)
        {
            if (product is null)
            {
                await products.AddAsync(
                    CatalogProduct.Register(
                        command.ProductId,
                        command.Sku!,
                        command.Name!,
                        command.Price!,
                        command.SourceVersion,
                        timeProvider
                    ),
                    cancellationToken
                );
            }
        }
        else if (product is null)
        {
            return Result<bool>.Failure(UnknownProduct);
        }
        else
        {
            Apply(product, command);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    private void Apply(CatalogProduct product, SyncCatalogProductCommand command)
    {
        _ = command.Kind switch
        {
            CatalogProductEventKind.PriceChanged => product.ApplyPrice(
                command.Price!,
                command.SourceVersion,
                timeProvider
            ),
            CatalogProductEventKind.StatusChanged => product.ApplyStatus(
                string.Equals(command.Status, "active", StringComparison.OrdinalIgnoreCase),
                command.SourceVersion,
                timeProvider
            ),
            _ => product.ApplyDeletion(command.SourceVersion, timeProvider),
        };
    }

    private static bool IsWellFormed(SyncCatalogProductCommand command) =>
        command.ProductId != Guid.Empty
        && command.SourceVersion >= 1
        && command.Kind switch
        {
            CatalogProductEventKind.Created => !string.IsNullOrWhiteSpace(command.Sku)
                && !string.IsNullOrWhiteSpace(command.Name)
                && command.Price is not null,
            CatalogProductEventKind.PriceChanged => command.Price is not null,
            CatalogProductEventKind.StatusChanged => !string.IsNullOrWhiteSpace(command.Status),
            CatalogProductEventKind.Deleted => true,
            _ => false,
        };
}
