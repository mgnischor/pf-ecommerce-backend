using Microsoft.EntityFrameworkCore;
using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IShoppingCartRepository"/>. Carts are always loaded with their lines; the soft-delete
/// filter of the entity conventions keeps deleted carts and removed lines out of every query; commits go through
/// <see cref="CartDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfShoppingCartRepository(CartDbContext context) : IShoppingCartRepository
{
    /// <inheritdoc />
    public Task<ShoppingCart?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.ShoppingCarts.Include(cart => cart.Items).FirstOrDefaultAsync(cart => cart.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ShoppingCart?> FindActiveByCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default
    ) =>
        context
            .ShoppingCarts.Include(cart => cart.Items)
            .FirstOrDefaultAsync(
                cart => cart.CustomerId == customerId && cart.Status == CartStatus.Active,
                cancellationToken
            );

    /// <inheritdoc />
    public async Task AddAsync(ShoppingCart aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.ShoppingCarts.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(ShoppingCart aggregate)
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
    public void Remove(ShoppingCart aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.ShoppingCarts.Remove(aggregate);
    }
}
