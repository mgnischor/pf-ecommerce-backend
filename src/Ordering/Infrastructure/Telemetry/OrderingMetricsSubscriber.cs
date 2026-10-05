using System.Diagnostics.Metrics;
using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>
/// Business KPIs of the Ordering context, derived from its domain events after the commit (ai/OBSERVABILITY.md §3.3,
/// §5.5): the aggregate has no telemetry code, and work that was rolled back is never counted. Dimensions are bounded
/// sets only: never an order number, a customer, a product, or the cancellation reason a client typed.
/// </summary>
internal sealed class OrderingMetricsSubscriber : IDomainEventSubscriber
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "ordering");

    private readonly Counter<long> _placed;
    private readonly Counter<long> _transitions;
    private readonly Counter<long> _cancelled;

    /// <summary>Creates the subscriber and its instruments.</summary>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    public OrderingMetricsSubscriber(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.ContextMeter("Ordering"));
        _placed = meter.CreateCounter<long>("ordering.orders.placed", unit: "{order}", description: "Orders placed.");
        _transitions = meter.CreateCounter<long>(
            "ordering.orders.transitions",
            unit: "{transition}",
            description: "Order lifecycle transitions, by the status they reached (paid, shipped, delivered)."
        );
        _cancelled = meter.CreateCounter<long>(
            "ordering.orders.cancelled",
            unit: "{order}",
            description: "Orders cancelled, by whether they had been paid."
        );
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case OrderPlaced:
                _placed.Add(1, Context);
                break;
            case OrderPaid:
                Transition("paid");
                break;
            case OrderShipped:
                Transition("shipped");
                break;
            case OrderDelivered:
                Transition("delivered");
                break;
            case OrderCancelled cancelled:
                _cancelled.Add(
                    1,
                    Context,
                    new KeyValuePair<string, object?>("ordering.order.was_paid", cancelled.WasPaid)
                );
                break;
        }

        return Task.CompletedTask;
    }

    private void Transition(string status) =>
        _transitions.Add(1, Context, new KeyValuePair<string, object?>("ordering.order.status", status));
}
