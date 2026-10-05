using Microsoft.EntityFrameworkCore;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>EF Core adapter of <see cref="IOrderReferenceRepository"/>; commits go through <see cref="ShippingDbContext"/>.</summary>
internal sealed class EfOrderReferenceRepository(ShippingDbContext context) : IOrderReferenceRepository
{
    /// <inheritdoc />
    public Task<OrderReference?> FindAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        context.OrderReferences.FirstOrDefaultAsync(reference => reference.Id == orderId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(OrderReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        await context.OrderReferences.AddAsync(reference, cancellationToken);
    }
}
