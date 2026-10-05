using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="Order"/> aggregate (with its lines).
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IOrderRepository : IRepository<Order>
{
    /// <summary>The order placed from a checkout (BR-ORD-001), or <c>null</c> when none was.</summary>
    /// <param name="checkoutId">The checkout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Order?> FindByCheckoutAsync(Guid checkoutId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves the next order number (BR-ORD-005): <c>PF-{year}-{sequence}</c>, from a database sequence, so concurrent
    /// placements never get the same one. A number taken by a placement that then fails is never reused.
    /// </summary>
    /// <param name="placedAt">The instant of the placement, which gives the year.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> NextNumberAsync(DateTimeOffset placedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a customer's orders in the order and from the position of <paramref name="criteria"/> (BR-ORD-006).
    /// Read-only: the orders are not tracked, and carry no lines.
    /// </summary>
    /// <param name="criteria">Owner, ordering, filters, page size, and position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Order>> ListAsync(OrderListCriteria criteria, CancellationToken cancellationToken = default);
}
