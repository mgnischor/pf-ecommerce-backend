using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Cart.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active carts only" contract.</summary>
internal sealed class FakeShoppingCartRepository : IShoppingCartRepository
{
    private readonly List<ShoppingCart> _carts = [];

    public IReadOnlyList<ShoppingCart> Carts => _carts;

    public int UpdateCalls { get; private set; }

    public Task<ShoppingCart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_carts.FirstOrDefault(cart => cart.Id == id && !cart.IsDeleted));

    public Task<ShoppingCart?> FindActiveByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _carts.FirstOrDefault(cart =>
                cart.CustomerId == customerId && cart.Status == CartStatus.Active && !cart.IsDeleted
            )
        );

    public Task AddAsync(ShoppingCart aggregate, CancellationToken cancellationToken = default)
    {
        _carts.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(ShoppingCart aggregate) => UpdateCalls++;

    public void Remove(ShoppingCart aggregate) => _carts.Remove(aggregate);

    public void Seed(ShoppingCart cart) => _carts.Add(cart);
}

/// <summary>In-memory stand-in for the Cart's view of the catalog.</summary>
internal sealed class FakeCatalogProductRepository : ICatalogProductRepository
{
    private readonly Dictionary<Guid, CatalogProduct> _products = [];

    public IReadOnlyCollection<CatalogProduct> Products => _products.Values;

    public Task<CatalogProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.GetValueOrDefault(id));

    public Task<IReadOnlyDictionary<Guid, CatalogProduct>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CatalogProduct>>(
            productIds.Where(_products.ContainsKey).Distinct().ToDictionary(id => id, id => _products[id])
        );

    public Task AddAsync(CatalogProduct aggregate, CancellationToken cancellationToken = default)
    {
        _products[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    public void Update(CatalogProduct aggregate) { }

    public void Remove(CatalogProduct aggregate) => _products.Remove(aggregate.Id);

    public void Seed(CatalogProduct product) => _products[product.Id] = product;
}

/// <summary>
/// Counts commits so tests can assert that failed use cases persist nothing. Given an inbox, a commit also commits the
/// rows the inbox staged, as the real unit of work does.
/// </summary>
/// <param name="inbox">The inbox committed together with the changes, if any.</param>
internal sealed class FakeUnitOfWork(FakeInbox? inbox = null) : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        inbox?.Commit();
        return Task.FromResult(1);
    }
}

/// <summary>
/// Like the inbox table: a message is begun in the delivery's transaction and only counts as handled once that
/// transaction commits. A delivery that fails leaves nothing behind, so its redelivery is handled, not skipped.
/// </summary>
internal sealed class FakeInbox : IInbox
{
    private readonly HashSet<(Guid MessageId, string Consumer)> _committed = [];
    private readonly HashSet<(Guid MessageId, string Consumer)> _staged = [];

    /// <summary>Messages begun in a delivery, committed or not.</summary>
    public int Begun => _committed.Count + _staged.Count;

    public Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_committed.Contains((messageId, consumer)) && _staged.Add((messageId, consumer)));

    /// <summary>The unit of work committed: what was staged now counts as handled.</summary>
    public void Commit()
    {
        _committed.UnionWith(_staged);
        _staged.Clear();
    }

    /// <summary>The delivery's scope ended without a commit: what it staged is gone.</summary>
    public void EndDelivery() => _staged.Clear();
}

/// <summary>Builds carts and catalog views with sensible defaults.</summary>
internal static class CartScenarios
{
    public static CatalogProduct Product(
        TimeProvider clock,
        decimal price = 100m,
        string currency = "BRL",
        bool sellable = true,
        string sku = "CAF-600-PRT",
        string name = "Cafeteira Elétrica"
    )
    {
        var product = CatalogProduct.Register(Guid.CreateVersion7(), sku, name, new Money(price, currency), 1, clock);
        if (sellable)
        {
            product.ApplyStatus(sellable: true, 2, clock);
        }

        return product;
    }

    public static ShoppingCart Cart(TimeProvider clock, Guid customerId, string currency = "BRL") =>
        ShoppingCart.Open(customerId, currency, clock).Value;
}
