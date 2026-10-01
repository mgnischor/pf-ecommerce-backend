using System.Collections.Concurrent;
using Portfolio.Identity.Domain;

namespace Portfolio.UnitTests.Identity.Support;

/// <summary>In-process stand-in for the EF Core refresh-token repository. See <see cref="InMemoryUserRepository"/>.</summary>
internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ConcurrentDictionary<Guid, RefreshToken> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byHash = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var token) && !token.IsDeleted ? token : null);

    /// <inheritdoc />
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        return Task.FromResult(
            _byHash.TryGetValue(tokenHash, out var id) && _byId.TryGetValue(id, out var token) ? token : null
        );
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<RefreshToken>>([.. _byId.Values.Where(token => token.FamilyId == familyId)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<RefreshToken>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<RefreshToken>>([.. _byId.Values.Where(token => token.UserId == userId)]);

    /// <inheritdoc />
    public Task AddAsync(RefreshToken aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        if (!_byHash.TryAdd(aggregate.TokenHash, aggregate.Id))
        {
            throw new InvalidOperationException("A refresh token with this hash already exists.");
        }

        _byId[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Update(RefreshToken aggregate)
    {
        // Entities are held by reference: changes are already visible.
    }

    /// <inheritdoc />
    public void Remove(RefreshToken aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _byId.TryRemove(aggregate.Id, out _);
        _byHash.TryRemove(aggregate.TokenHash, out _);
    }
}
