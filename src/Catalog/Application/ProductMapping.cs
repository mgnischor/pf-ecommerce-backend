using Portfolio.Catalog.Domain;

namespace Portfolio.Catalog.Application;

/// <summary>Projects the aggregate onto its read models.</summary>
internal static class ProductMapping
{
    /// <summary>Builds the full view of <paramref name="product"/>.</summary>
    /// <param name="product">Product.</param>
    public static ProductView ToView(this Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductView(
            product.Id,
            product.Name,
            product.Description,
            product.Sku.Value,
            product.Price.Amount,
            product.Price.Currency,
            product.Status.ToView(),
            product.Version,
            AllowedActionsOf(product.Status),
            product.CreatedAt,
            product.UpdatedAt
        );
    }

    /// <summary>Maps a domain status to the status of the read models.</summary>
    /// <param name="status">Domain status.</param>
    public static ProductStatusView ToView(this ProductStatus status) =>
        status switch
        {
            ProductStatus.Draft => ProductStatusView.Draft,
            ProductStatus.Active => ProductStatusView.Active,
            ProductStatus.Discontinued => ProductStatusView.Discontinued,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown product status."),
        };

    /// <summary>Builds the collection view of <paramref name="product"/>.</summary>
    /// <param name="product">Product.</param>
    public static ProductSummaryView ToSummary(this Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new ProductSummaryView(
            product.Id,
            product.Name,
            product.Sku.Value,
            product.Price.Amount,
            product.Price.Currency,
            product.Status.ToView()
        );
    }

    /// <summary>Maps a read-model status back to the domain status.</summary>
    /// <param name="status">Read-model status.</param>
    public static ProductStatus ToDomain(this ProductStatusView status) =>
        status switch
        {
            ProductStatusView.Draft => ProductStatus.Draft,
            ProductStatusView.Active => ProductStatus.Active,
            ProductStatusView.Discontinued => ProductStatus.Discontinued,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown product status."),
        };

    private static string[] AllowedActionsOf(ProductStatus status) =>
        status switch
        {
            ProductStatus.Draft =>
            [
                ProductActions.Update,
                ProductActions.ChangePrice,
                ProductActions.Activate,
                ProductActions.Delete,
            ],
            ProductStatus.Active =>
            [
                ProductActions.Update,
                ProductActions.ChangePrice,
                ProductActions.Discontinue,
                ProductActions.Delete,
            ],
            _ => [ProductActions.Update, ProductActions.ChangePrice, ProductActions.Delete],
        };
}
