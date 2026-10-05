using Microsoft.EntityFrameworkCore;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IShipmentRepository"/>. The soft-delete filter of the entity conventions keeps deleted
/// shipments out of every query; commits go through <see cref="ShippingDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfShipmentRepository(ShippingDbContext context) : IShipmentRepository
{
    /// <inheritdoc />
    public Task<Shipment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Shipments.FirstOrDefaultAsync(shipment => shipment.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Shipment?> FindByOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        context.Shipments.FirstOrDefaultAsync(shipment => shipment.OrderId == orderId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Shipment>> ListByOrderAsync(
        Guid orderId,
        Guid customerId,
        int limit,
        ShipmentSeekPosition? after,
        CancellationToken cancellationToken = default
    ) => await BuildListQuery(orderId, customerId, limit, after).ToListAsync(cancellationToken);

    /// <summary>The query a listing runs. Exposed so its SQL can be checked without a database.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The customer; only their shipments are ever returned.</param>
    /// <param name="limit">Maximum number of shipments to return.</param>
    /// <param name="after">Continue after this position, or <c>null</c> for the first page.</param>
    internal IQueryable<Shipment> BuildListQuery(Guid orderId, Guid customerId, int limit, ShipmentSeekPosition? after)
    {
        // Ownership is part of the query itself: nothing a caller passes can widen it to another customer's shipments.
        var query = context
            .Shipments.AsNoTracking()
            .Where(shipment => shipment.OrderId == orderId && shipment.CustomerId == customerId);

        if (after is not null)
        {
            var id = after.Id;
            var createdAt = after.CreatedAt;
            query = query.Where(shipment =>
                shipment.CreatedAt > createdAt || (shipment.CreatedAt == createdAt && shipment.Id.CompareTo(id) > 0)
            );
        }

        return query.OrderBy(shipment => shipment.CreatedAt).ThenBy(shipment => shipment.Id).Take(limit);
    }

    /// <inheritdoc />
    public async Task AddAsync(Shipment aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.Shipments.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(Shipment aggregate)
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
    public void Remove(Shipment aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.Shipments.Remove(aggregate);
    }
}
