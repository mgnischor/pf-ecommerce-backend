using System.Diagnostics.Metrics;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>
/// Business KPIs of the Shipping context, derived from its domain events after the commit (ai/OBSERVABILITY.md §3.3,
/// §5.5): the aggregate has no telemetry code, and work that was rolled back is never counted. Dimensions are bounded
/// sets only: never a carrier name, a tracking code, an order, or a customer.
/// </summary>
internal sealed class ShippingMetricsSubscriber : IDomainEventSubscriber
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "shipping");

    private readonly Counter<long> _transitions;

    /// <summary>Creates the subscriber and its instruments.</summary>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    public ShippingMetricsSubscriber(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.ContextMeter("Shipping"));
        _transitions = meter.CreateCounter<long>(
            "shipping.shipments.transitions",
            unit: "{transition}",
            description: "Shipment lifecycle transitions, by the status they reached."
        );
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var status = domainEvent switch
        {
            ShipmentDispatched => "in_transit",
            ShipmentDelivered => "delivered",
            ShipmentFailed => "failed",
            ShipmentCancelled => "cancelled",
            _ => null,
        };

        if (status is not null)
        {
            _transitions.Add(1, Context, new KeyValuePair<string, object?>("shipping.shipment.status", status));
        }

        return Task.CompletedTask;
    }
}
