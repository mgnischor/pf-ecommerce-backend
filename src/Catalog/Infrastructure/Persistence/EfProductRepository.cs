using Microsoft.EntityFrameworkCore;
using Portfolio.Catalog.Domain;

namespace Portfolio.Catalog.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IProductRepository"/>. The soft-delete filter of the entity conventions keeps
/// deleted products out of every query; commits go through <see cref="CatalogDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfProductRepository(CatalogDbContext context) : IProductRepository
{
    /// <inheritdoc />
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sku);
        return context.Products.FirstOrDefaultAsync(product => product.Sku == sku, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Product?> FindByCreationKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);

        // A product deleted since is still the answer to its own retry, so the soft-delete filter is lifted.
        return context
            .Products.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.CreationKey == idempotencyKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> WasDeletedByAsync(
        Guid productId,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);

        return context
            .Products.IgnoreQueryFilters()
            .AnyAsync(
                product =>
                    product.Id == productId && product.DeletedAt != null && product.DeletionKey == idempotencyKey,
                cancellationToken
            );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> ListAsync(
        ProductListCriteria criteria,
        CancellationToken cancellationToken = default
    ) => await BuildListQuery(criteria).ToListAsync(cancellationToken);

    /// <summary>The query a listing runs. Exposed so its SQL can be checked without a database.</summary>
    /// <param name="criteria">Ordering, filters, page size, and position.</param>
    internal IQueryable<Product> BuildListQuery(ProductListCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var query = context.Products.AsNoTracking();

        if (criteria.Status is { } status)
        {
            query = query.Where(product => product.Status == status);
        }

        if (criteria.SearchText is { } text)
        {
            query = Search(query, text);
        }

        return Order(Seek(query, criteria), criteria).Take(criteria.Limit);
    }

    // Name: case-insensitive substring. SKU: exact, because the column is mapped through a value converter and so
    // cannot take part in a pattern match; a text that is not a valid SKU simply cannot match one.
    private static IQueryable<Product> Search(IQueryable<Product> query, string text)
    {
        var pattern =
            "%"
            + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
            + "%";

        var sku = Sku.Create(text);
        if (sku.IsFailure)
        {
            return query.Where(product => EF.Functions.ILike(product.Name, pattern, "\\"));
        }

        var exactSku = sku.Value;
        return query.Where(product => EF.Functions.ILike(product.Name, pattern, "\\") || product.Sku == exactSku);
    }

    // Keyset: continue strictly after the last item of the previous page in the listing order, so that pages never
    // duplicate or skip an item even while products are created or deleted.
    private static IQueryable<Product> Seek(IQueryable<Product> query, ProductListCriteria criteria)
    {
        if (criteria.After is not { } after)
        {
            return query;
        }

        var id = after.Id;
        switch (criteria.SortField)
        {
            case ProductSortField.Name when after.Name is { } name:
                return criteria.Descending
                    ? query.Where(product =>
                        product.Name.CompareTo(name) < 0 || (product.Name == name && product.Id.CompareTo(id) < 0)
                    )
                    : query.Where(product =>
                        product.Name.CompareTo(name) > 0 || (product.Name == name && product.Id.CompareTo(id) > 0)
                    );
            case ProductSortField.Price when after.PriceAmount is { } amount:
                return criteria.Descending
                    ? query.Where(product =>
                        product.Price.Amount < amount
                        || (product.Price.Amount == amount && product.Id.CompareTo(id) < 0)
                    )
                    : query.Where(product =>
                        product.Price.Amount > amount
                        || (product.Price.Amount == amount && product.Id.CompareTo(id) > 0)
                    );
            case ProductSortField.CreatedAt when after.CreatedAt is { } createdAt:
                return criteria.Descending
                    ? query.Where(product =>
                        product.CreatedAt < createdAt
                        || (product.CreatedAt == createdAt && product.Id.CompareTo(id) < 0)
                    )
                    : query.Where(product =>
                        product.CreatedAt > createdAt
                        || (product.CreatedAt == createdAt && product.Id.CompareTo(id) > 0)
                    );
            default:
                throw new ArgumentException("The seek position does not match the sort field.", nameof(criteria));
        }
    }

    private static IQueryable<Product> Order(IQueryable<Product> query, ProductListCriteria criteria) =>
        (criteria.SortField, criteria.Descending) switch
        {
            (ProductSortField.Name, true) => query
                .OrderByDescending(product => product.Name)
                .ThenByDescending(product => product.Id),
            (ProductSortField.Name, false) => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
            (ProductSortField.Price, true) => query
                .OrderByDescending(product => product.Price.Amount)
                .ThenByDescending(product => product.Id),
            (ProductSortField.Price, false) => query
                .OrderBy(product => product.Price.Amount)
                .ThenBy(product => product.Id),
            (_, true) => query.OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id),
            _ => query.OrderBy(product => product.CreatedAt).ThenBy(product => product.Id),
        };

    /// <inheritdoc />
    public async Task AddAsync(Product aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.Products.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(Product aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // Aggregates are loaded through the repository, so they are tracked and need no staging. Attaching a detached
        // one would carry its current version as the "original" and bypass the optimistic-concurrency check.
        if (context.Entry(aggregate).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Only an aggregate loaded through the repository can be updated.");
        }
    }

    /// <inheritdoc />
    public void Remove(Product aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.Products.Remove(aggregate);
    }
}
