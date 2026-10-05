using Microsoft.EntityFrameworkCore;
using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>EF Core adapter of <see cref="ICatalogProductRepository"/>; commits go through <see cref="CartDbContext"/>.</summary>
internal sealed class EfCatalogProductRepository(CartDbContext context) : ICatalogProductRepository
{
    /// <inheritdoc />
    public Task<CatalogProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.CatalogProducts.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, CatalogProduct>();
        }

        var ids = productIds.Distinct().ToArray();
        var found = await context
            .CatalogProducts.AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .ToListAsync(cancellationToken);

        return found.ToDictionary(product => product.Id);
    }

    /// <inheritdoc />
    public async Task AddAsync(CatalogProduct aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.CatalogProducts.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(CatalogProduct aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // Aggregates are loaded through the repository, so they are tracked and need no staging.
        if (context.Entry(aggregate).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Only an aggregate loaded through the repository can be updated.");
        }
    }

    /// <inheritdoc />
    public void Remove(CatalogProduct aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.CatalogProducts.Remove(aggregate);
    }
}
