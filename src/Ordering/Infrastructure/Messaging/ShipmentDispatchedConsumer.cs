using Portfolio.Ordering.Application;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>Consumes <c>shipping.shipment-dispatched</c>: the carrier took the order's shipment, so the order is shipped (BR-ORD-003).</summary>
internal sealed class ShipmentDispatchedConsumer()
    : ShipmentProgressConsumer(
        "ordering.mark-shipped-on-shipment-dispatched",
        "shipping.shipment-dispatched",
        OrderMilestone.Shipped
    );
