using Microsoft.EntityFrameworkCore;
using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IRefreshTokenRepository"/>. Two requests presenting the same token race on the
/// <c>version</c> column: the loser's commit fails with a concurrency conflict instead of consuming it twice.
/// </summary>
internal sealed class EfRefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    /// <inheritdoc />
    public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.FirstOrDefaultAsync(token => token.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        return context.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default
    ) => await context.RefreshTokens.Where(token => token.FamilyId == familyId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshToken>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    ) => await context.RefreshTokens.Where(token => token.UserId == userId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(RefreshToken aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.RefreshTokens.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(RefreshToken aggregate)
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
    public void Remove(RefreshToken aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.RefreshTokens.Remove(aggregate);
    }
}
