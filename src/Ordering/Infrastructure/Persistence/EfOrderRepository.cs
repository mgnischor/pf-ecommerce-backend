using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Portfolio.Ordering.Domain;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>
/// EF Core adapter of <see cref="IOrderRepository"/>. Orders are loaded with their lines for the use cases; a listing
/// carries no lines. The soft-delete filter of the entity conventions keeps deleted orders out of every query; commits go
/// through <see cref="OrderingDbContext"/> as the unit of work.
/// </summary>
internal sealed class EfOrderRepository(OrderingDbContext context) : IOrderRepository
{
    /// <inheritdoc />
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Orders.Include(order => order.Items).FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Order?> FindByCheckoutAsync(Guid checkoutId, CancellationToken cancellationToken = default) =>
        context
            .Orders.Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.CheckoutId == checkoutId, cancellationToken);

    /// <inheritdoc />
    public async Task<string> NextNumberAsync(DateTimeOffset placedAt, CancellationToken cancellationToken = default)
    {
        // A constant statement with no value spliced into it: the sequence name is a literal.
        var next = await context
            .Database.SqlQuery<long>($"SELECT nextval('ordering.order_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"PF-{placedAt.UtcDateTime.Year}-{next:D6}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Order>> ListAsync(
        OrderListCriteria criteria,
        CancellationToken cancellationToken = default
    ) => await BuildListQuery(criteria).ToListAsync(cancellationToken);

    /// <summary>The query a listing runs. Exposed so its SQL can be checked without a database.</summary>
    /// <param name="criteria">Owner, ordering, filters, page size, and position.</param>
    internal IQueryable<Order> BuildListQuery(OrderListCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Ownership is part of the query itself: nothing a caller passes can widen it to another customer's orders.
        var customerId = criteria.CustomerId;
        var query = context.Orders.AsNoTracking().Where(order => order.CustomerId == customerId);

        if (criteria.Status is { } status)
        {
            query = query.Where(order => order.Status == status);
        }

        if (criteria.PlacedFrom is { } placedFrom)
        {
            query = query.Where(order => order.PlacedAt >= placedFrom);
        }

        return Sorted(Seek(query, criteria), criteria).Take(criteria.Limit);
    }

    // Keyset: continue strictly after the last order of the previous page in the listing order, so that pages never
    // duplicate or skip an order even while orders are placed.
    private static IQueryable<Order> Seek(IQueryable<Order> query, OrderListCriteria criteria)
    {
        if (criteria.After is not { } after)
        {
            return query;
        }

        var id = after.Id;
        switch (criteria.SortField)
        {
            case OrderSortField.Total when after.TotalAmount is { } amount:
                return criteria.Descending
                    ? query.Where(order =>
                        order.Total.Amount < amount || (order.Total.Amount == amount && order.Id.CompareTo(id) < 0)
                    )
                    : query.Where(order =>
                        order.Total.Amount > amount || (order.Total.Amount == amount && order.Id.CompareTo(id) > 0)
                    );
            case OrderSortField.PlacedAt when after.PlacedAt is { } placedAt:
                return criteria.Descending
                    ? query.Where(order =>
                        order.PlacedAt < placedAt || (order.PlacedAt == placedAt && order.Id.CompareTo(id) < 0)
                    )
                    : query.Where(order =>
                        order.PlacedAt > placedAt || (order.PlacedAt == placedAt && order.Id.CompareTo(id) > 0)
                    );
            default:
                throw new ArgumentException("The seek position does not match the sort field.", nameof(criteria));
        }
    }

    private static IQueryable<Order> Sorted(IQueryable<Order> query, OrderListCriteria criteria) =>
        (criteria.SortField, criteria.Descending) switch
        {
            (OrderSortField.Total, true) => query
                .OrderByDescending(order => order.Total.Amount)
                .ThenByDescending(order => order.Id),
            (OrderSortField.Total, false) => query.OrderBy(order => order.Total.Amount).ThenBy(order => order.Id),
            (_, true) => query.OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id),
            _ => query.OrderBy(order => order.PlacedAt).ThenBy(order => order.Id),
        };

    /// <inheritdoc />
    public async Task AddAsync(Order aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        await context.Orders.AddAsync(aggregate, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(Order aggregate)
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
    public void Remove(Order aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // The auditing interceptor turns the deletion into a logical one (deleted_at).
        context.Orders.Remove(aggregate);
    }
}
