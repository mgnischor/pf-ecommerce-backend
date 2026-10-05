using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="Shipment"/> aggregate.
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IShipmentRepository : IRepository<Shipment>
{
    /// <summary>The shipment of an order (BR-SHP-001: there is at most one), or <c>null</c> when there is none.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Shipment?> FindByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shipments of an order that belong to a customer, oldest first with the identifier as tie-breaker
    /// (BR-SHP-004). The owner is part of the query itself. Read-only: the shipments are not tracked.
    /// </summary>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The customer; only their shipments are ever returned.</param>
    /// <param name="limit">Maximum number of shipments to return.</param>
    /// <param name="after">Continue after this position, or <c>null</c> for the first page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Shipment>> ListByOrderAsync(
        Guid orderId,
        Guid customerId,
        int limit,
        ShipmentSeekPosition? after,
        CancellationToken cancellationToken = default
    );
}
