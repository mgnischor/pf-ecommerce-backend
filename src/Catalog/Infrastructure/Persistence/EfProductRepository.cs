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
