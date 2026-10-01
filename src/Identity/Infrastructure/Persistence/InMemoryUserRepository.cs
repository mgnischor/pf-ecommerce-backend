using System.Collections.Concurrent;
using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Temporary in-process store for accounts, registered as a singleton until the EF Core mapping,
/// migrations, and the Identity schema exist (ai/TASKS.md, Database). Data is lost on restart and is not
/// shared between instances; it is deliberately the only part of Identity that is not production-ready.
/// </summary>
internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byEmail = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var user) && !user.IsDeleted ? user : null);

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        return Task.FromResult(
            _byEmail.TryGetValue(email.Value, out var id) && _byId.TryGetValue(id, out var user) && !user.IsDeleted
                ? user
                : null
        );
    }

    /// <inheritdoc />
    public Task AddAsync(User aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The unique index on the e-mail is what closes the race between two concurrent registrations.
        if (!_byEmail.TryAdd(aggregate.Email.Value, aggregate.Id))
        {
            throw new InvalidOperationException("An account with this e-mail already exists.");
        }

        _byId[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(User aggregate)
    {
        // Entities are held by reference: changes are already visible.
    }

    /// <inheritdoc />
    public void Remove(User aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _byId.TryRemove(aggregate.Id, out _);
        _byEmail.TryRemove(aggregate.Email.Value, out _);
    }
}
