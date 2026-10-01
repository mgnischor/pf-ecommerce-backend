using System.Collections.Concurrent;
using Portfolio.Identity.Domain;

namespace Portfolio.UnitTests.Identity.Support;

/// <summary>
/// In-process stand-in for the EF Core account repository, so handler tests run without a database
/// (ai/TESTS.md section 3.1). Honours the active-accounts-only contract and the unique e-mail.
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
