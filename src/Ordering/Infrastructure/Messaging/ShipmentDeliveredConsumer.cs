using Portfolio.Ordering.Application;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>Consumes <c>shipping.shipment-delivered</c>: the order's shipment reached the customer, so the order is delivered (BR-ORD-003).</summary>
internal sealed class ShipmentDeliveredConsumer()
    : ShipmentProgressConsumer(
        "ordering.mark-delivered-on-shipment-delivered",
        "shipping.shipment-delivered",
        OrderMilestone.Delivered
    );
