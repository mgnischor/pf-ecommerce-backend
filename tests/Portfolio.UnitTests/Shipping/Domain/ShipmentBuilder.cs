using Portfolio.Shipping.Domain;

namespace Portfolio.UnitTests.Shipping.Domain;

/// <summary>
/// Builds valid shipments in any status; tests override only what they assert on (ai/TESTS.md §10). The returned
/// shipment has no pending domain events, so assertions see only what the test provokes.
/// </summary>
internal sealed class ShipmentBuilder
{
    private Guid _orderId = Guid.CreateVersion7();
    private Guid _customerId = Guid.CreateVersion7();
    private ShipmentStatus _status = ShipmentStatus.Preparing;

    public static ShipmentBuilder New() => new();

    public ShipmentBuilder ForOrder(Guid orderId, Guid customerId)
    {
        _orderId = orderId;
        _customerId = customerId;
        return this;
    }

    public ShipmentBuilder WithStatus(ShipmentStatus status)
    {
        _status = status;
        return this;
    }

    public Shipment Build(TimeProvider timeProvider)
    {
        var shipment = Shipment.Prepare(_orderId, _customerId, timeProvider).Value;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (_status is ShipmentStatus.InTransit or ShipmentStatus.Delivered or ShipmentStatus.Failed)
        {
            shipment.Dispatch("Correios", "BR123456789", today.AddDays(3), timeProvider);
        }

        if (_status is ShipmentStatus.Delivered)
        {
            shipment.MarkDelivered(timeProvider);
        }

        if (_status is ShipmentStatus.Failed)
        {
            shipment.MarkFailed("addressNotFound", timeProvider);
        }

        if (_status is ShipmentStatus.Cancelled)
        {
            shipment.Cancel(timeProvider);
        }

        shipment.ClearDomainEvents();
        return shipment;
    }
}
