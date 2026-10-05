namespace Portfolio.Shipping.Domain;

/// <summary>
/// Persistence abstraction for <see cref="OrderReference"/>. Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IOrderReferenceRepository
{
    /// <summary>The record of an order, or <c>null</c> when the context has not heard of it.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OrderReference?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new record for insertion.</summary>
    /// <param name="reference">Record to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(OrderReference reference, CancellationToken cancellationToken = default);
}
