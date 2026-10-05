using Portfolio.SharedKernel.Application;
using Portfolio.Shipping.Domain;

namespace Portfolio.UnitTests.Shipping.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active shipments only" contract.</summary>
internal sealed class FakeShipmentRepository : IShipmentRepository
{
    private readonly List<Shipment> _shipments = [];

    public IReadOnlyList<Shipment> Shipments => _shipments;

    public int UpdateCalls { get; private set; }

    /// <summary>The position of the last <see cref="ListByOrderAsync"/> call.</summary>
    public ShipmentSeekPosition? LastAfter { get; private set; }

    /// <summary>The page size of the last <see cref="ListByOrderAsync"/> call.</summary>
    public int? LastLimit { get; private set; }

    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_shipments.FirstOrDefault(shipment => shipment.Id == id && !shipment.IsDeleted));

    public Task<Shipment?> FindByOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_shipments.FirstOrDefault(shipment => shipment.OrderId == orderId));

    public Task<IReadOnlyList<Shipment>> ListByOrderAsync(
        Guid orderId,
        Guid customerId,
        int limit,
        ShipmentSeekPosition? after,
        CancellationToken cancellationToken = default
    )
    {
        LastAfter = after;
        LastLimit = limit;

        var found = _shipments
            .Where(shipment => shipment.OrderId == orderId && shipment.CustomerId == customerId)
            .OrderBy(shipment => shipment.CreatedAt)
            .ThenBy(shipment => shipment.Id)
            .Where(shipment =>
                after is null
                || shipment.CreatedAt > after.CreatedAt
                || (shipment.CreatedAt == after.CreatedAt && shipment.Id.CompareTo(after.Id) > 0)
            )
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<Shipment>>(found);
    }

    public Task AddAsync(Shipment aggregate, CancellationToken cancellationToken = default)
    {
        _shipments.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(Shipment aggregate) => UpdateCalls++;

    public void Remove(Shipment aggregate) => _shipments.Remove(aggregate);

    public void Seed(Shipment shipment) => _shipments.Add(shipment);
}

/// <summary>In-memory stand-in for the repository of the context's order records.</summary>
internal sealed class FakeOrderReferenceRepository : IOrderReferenceRepository
{
    private readonly Dictionary<Guid, OrderReference> _references = [];

    public IReadOnlyCollection<OrderReference> References => _references.Values;

    public Task<OrderReference?> FindAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_references.GetValueOrDefault(orderId));

    public Task AddAsync(OrderReference reference, CancellationToken cancellationToken = default)
    {
        _references[reference.Id] = reference;
        return Task.CompletedTask;
    }

    public void Seed(OrderReference reference) => _references[reference.Id] = reference;
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
