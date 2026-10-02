using System.Diagnostics.Metrics;
using Portfolio.Inventory.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>
/// Business KPIs of the Inventory context, derived from its domain events after the commit (ai/OBSERVABILITY.md §3.3, §5.5):
/// the aggregate has no telemetry code, and work that was rolled back is never counted. Dimensions are bounded sets only
/// (never a SKU: ai/OBSERVABILITY.md §5.4).
/// </summary>
internal sealed class InventoryMetricsSubscriber : IDomainEventSubscriber
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "inventory");

    private readonly Counter<long> _adjustments;
    private readonly Counter<long> _itemsOpened;
    private readonly Counter<long> _reservationsCreated;
    private readonly Counter<long> _reservationsReleased;

    /// <summary>Creates the subscriber and its instruments.</summary>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    public InventoryMetricsSubscriber(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.ContextMeter("Inventory"));
        _adjustments = meter.CreateCounter<long>(
            "inventory.stock.adjustments",
            unit: "{adjustment}",
            description: "Manual stock adjustments recorded, by direction (increase or decrease)."
        );
        _itemsOpened = meter.CreateCounter<long>(
            "inventory.items.opened",
            unit: "{item}",
            description: "Inventory items opened for a SKU."
        );
        _reservationsCreated = meter.CreateCounter<long>(
            "inventory.reservations.created",
            unit: "{reservation}",
            description: "Stock reservations made."
        );
        _reservationsReleased = meter.CreateCounter<long>(
            "inventory.reservations.released",
            unit: "{reservation}",
            description: "Stock reservations released."
        );
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case StockAdjusted adjusted:
                _adjustments.Add(
                    1,
                    Context,
                    new KeyValuePair<string, object?>(
                        "inventory.adjustment.direction",
                        adjusted.Delta > 0 ? "increase" : "decrease"
                    )
                );
                break;
            case InventoryItemOpened:
                _itemsOpened.Add(1, Context);
                break;
            case StockReserved:
                _reservationsCreated.Add(1, Context);
                break;
            case StockReleased:
                _reservationsReleased.Add(1, Context);
                break;
        }

        return Task.CompletedTask;
    }
}
