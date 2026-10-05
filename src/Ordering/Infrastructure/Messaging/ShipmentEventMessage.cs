namespace Portfolio.Ordering.Infrastructure;

/// <summary>
/// Ordering's reading of the Shipping's shipment events (<c>shipment-dispatched</c>, <c>shipment-delivered</c>): only the
/// order the shipment fulfills. Other fields in the payload are ignored.
/// </summary>
/// <param name="OrderId">The order the shipment fulfills.</param>
internal sealed record ShipmentEventMessage(Guid OrderId);
