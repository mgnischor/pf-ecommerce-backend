using Microsoft.EntityFrameworkCore;
using Portfolio.Customers.Domain;

namespace Portfolio.Customers.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="ICustomerProfileRepository"/>. The soft-delete filter of the entity conventions keeps
/// deleted profiles out of every query; commits go through <see cref="CustomersDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfCustomerProfileRepository(CustomersDbContext context) : ICustomerProfileRepository
{
    /// <inheritdoc />
    public Task<CustomerProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.CustomerProfiles.FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(CustomerProfile aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.CustomerProfiles.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(CustomerProfile aggregate)
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
    public void Remove(CustomerProfile aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.CustomerProfiles.Remove(aggregate);
    }
}
