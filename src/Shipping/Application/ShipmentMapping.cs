using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Application;

/// <summary>Projects the aggregate onto its read model.</summary>
internal static class ShipmentMapping
{
    /// <summary>Builds the view of <paramref name="shipment"/>.</summary>
    /// <param name="shipment">Shipment.</param>
    public static ShipmentView ToView(this Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentView(
            shipment.Id,
            shipment.OrderId,
            shipment.Status.ToView(),
            shipment.Carrier,
            shipment.TrackingCode,
            shipment.EstimatedDeliveryDate
        );
    }

    /// <summary>Maps a domain status to the status of the read model.</summary>
    /// <param name="status">Domain status.</param>
    public static ShipmentStatusView ToView(this ShipmentStatus status) =>
        status switch
        {
            ShipmentStatus.Preparing => ShipmentStatusView.Preparing,
            ShipmentStatus.InTransit => ShipmentStatusView.InTransit,
            ShipmentStatus.Delivered => ShipmentStatusView.Delivered,
            ShipmentStatus.Failed => ShipmentStatusView.Failed,
            ShipmentStatus.Cancelled => ShipmentStatusView.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown shipment status."),
        };
}
