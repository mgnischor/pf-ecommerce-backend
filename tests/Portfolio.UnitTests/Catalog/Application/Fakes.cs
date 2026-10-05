using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.Catalog.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active products only" contract.</summary>
internal sealed class FakeProductRepository : IProductRepository
{
    private readonly List<Product> _products = [];

    public IReadOnlyList<Product> Products => _products;

    public int UpdateCalls { get; private set; }

    /// <summary>What <see cref="ListAsync"/> answers; the real ordering and filtering are checked against the SQL.</summary>
    public List<Product> ListResult { get; } = [];

    /// <summary>The criteria of the last <see cref="ListAsync"/> call.</summary>
    public ProductListCriteria? LastCriteria { get; private set; }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.FirstOrDefault(product => product.Id == id && !product.IsDeleted));

    public Task<Product?> FindBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.FirstOrDefault(product => product.Sku == sku && !product.IsDeleted));

    public Task<Product?> FindByCreationKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _products.FirstOrDefault(product =>
                string.Equals(product.CreationKey, idempotencyKey, StringComparison.Ordinal)
            )
        );

    public Task<bool> WasDeletedByAsync(
        Guid productId,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _products.Any(product =>
                product.Id == productId
                && product.IsDeleted
                && string.Equals(product.DeletionKey, idempotencyKey, StringComparison.Ordinal)
            )
        );

    public Task<IReadOnlyList<Product>> ListAsync(
        ProductListCriteria criteria,
        CancellationToken cancellationToken = default
    )
    {
        LastCriteria = criteria;
        return Task.FromResult<IReadOnlyList<Product>>(ListResult.Take(criteria.Limit).ToList());
    }

    public Task AddAsync(Product aggregate, CancellationToken cancellationToken = default)
    {
        _products.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(Product aggregate) => UpdateCalls++;

    public void Remove(Product aggregate) => _products.Remove(aggregate);

    public void Seed(Product product) => _products.Add(product);
}

/// <summary>Counts commits so tests can assert that failed use cases persist nothing.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}
