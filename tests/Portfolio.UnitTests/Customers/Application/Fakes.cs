using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Application;

namespace Portfolio.UnitTests.Customers.Application;

/// <summary>In-memory stand-in for the EF Core repository; honours the "active profiles only" contract.</summary>
internal sealed class FakeCustomerProfileRepository : ICustomerProfileRepository
{
    private readonly List<CustomerProfile> _profiles = [];

    public IReadOnlyList<CustomerProfile> Profiles => _profiles;

    public int UpdateCalls { get; private set; }

    public Task<CustomerProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_profiles.FirstOrDefault(profile => profile.Id == id && !profile.IsDeleted));

    public Task AddAsync(CustomerProfile aggregate, CancellationToken cancellationToken = default)
    {
        _profiles.Add(aggregate);
        return Task.CompletedTask;
    }

    public void Update(CustomerProfile aggregate) => UpdateCalls++;

    public void Remove(CustomerProfile aggregate) => _profiles.Remove(aggregate);

    public void Seed(CustomerProfile profile) => _profiles.Add(profile);
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

/// <summary>Remembers which (message, consumer) pairs were begun, like the inbox table does.</summary>
internal sealed class FakeInbox : IInbox
{
    private readonly HashSet<(Guid MessageId, string Consumer)> _seen = [];

    public int Begun => _seen.Count;

    public Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default) =>
        Task.FromResult(_seen.Add((messageId, consumer)));
}
