using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.Ordering.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active orders only" contract.</summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = [];
    private int _lastNumber;

    public IReadOnlyList<Order> Orders => _orders;

    public int UpdateCalls { get; private set; }

    /// <summary>How many order numbers were drawn, whether or not a placement used them.</summary>
    public int NumbersDrawn => _lastNumber;

    /// <summary>What <see cref="ListAsync"/> answers; the real filtering and ordering are checked against the SQL.</summary>
    public List<Order> ListResult { get; } = [];

    /// <summary>The criteria of the last <see cref="ListAsync"/> call.</summary>
    public OrderListCriteria? LastCriteria { get; private set; }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orders.FirstOrDefault(order => order.Id == id && !order.IsDeleted));

    public Task<Order?> FindByCheckoutAsync(Guid checkoutId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orders.FirstOrDefault(order => order.CheckoutId == checkoutId));

    public Task<string> NextNumberAsync(DateTimeOffset placedAt, CancellationToken cancellationToken = default) =>
        Task.FromResult($"PF-{placedAt.Year}-{++_lastNumber:D6}");

    public Task<IReadOnlyList<Order>> ListAsync(
        OrderListCriteria criteria,
        CancellationToken cancellationToken = default
    )
    {
        LastCriteria = criteria;
        return Task.FromResult<IReadOnlyList<Order>>(ListResult.Take(criteria.Limit).ToList());
    }

    public Task AddAsync(Order aggregate, CancellationToken cancellationToken = default)
    {
        _orders.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(Order aggregate) => UpdateCalls++;

    public void Remove(Order aggregate) => _orders.Remove(aggregate);

    public void Seed(Order order) => _orders.Add(order);
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

    public int Begun => _committed.Count + _staged.Count;

    public Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_committed.Contains((messageId, consumer)) && _staged.Add((messageId, consumer)));

    public void Commit()
    {
        _committed.UnionWith(_staged);
        _staged.Clear();
    }

    public void EndDelivery() => _staged.Clear();
}
