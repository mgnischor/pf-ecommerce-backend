using Microsoft.EntityFrameworkCore;
using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IUserRepository"/>. The soft-delete filter of the entity conventions keeps
/// deleted accounts out of every query; commits go through <see cref="IdentityDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfUserRepository(IdentityDbContext context) : IUserRepository
{
    /// <inheritdoc />
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        return context.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(User aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.Users.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(User aggregate)
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
    public void Remove(User aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.Users.Remove(aggregate);
    }
}
