using System.Diagnostics.Metrics;
using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.Catalog.Infrastructure;

/// <summary>
/// Business KPIs of the Catalog context, derived from its domain events after the commit (ai/OBSERVABILITY.md §3.3, §5.5).
/// Dimensions are bounded sets only: never a SKU, a name or a price.
/// </summary>
internal sealed class CatalogMetricsSubscriber : IDomainEventSubscriber
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "catalog");

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusChanges;
    private readonly Counter<long> _priceChanges;
    private readonly Counter<long> _deleted;

    /// <summary>Creates the subscriber and its instruments.</summary>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    public CatalogMetricsSubscriber(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.ContextMeter("Catalog"));
        _created = meter.CreateCounter<long>(
            "catalog.products.created",
            unit: "{product}",
            description: "Products added to the catalog."
        );
        _statusChanges = meter.CreateCounter<long>(
            "catalog.products.status_changes",
            unit: "{change}",
            description: "Product lifecycle transitions, by the status they reached."
        );
        _priceChanges = meter.CreateCounter<long>(
            "catalog.products.price_changes",
            unit: "{change}",
            description: "Sell prices changed."
        );
        _deleted = meter.CreateCounter<long>(
            "catalog.products.deleted",
            unit: "{product}",
            description: "Products logically deleted."
        );
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case ProductCreated:
                _created.Add(1, Context);
                break;
            case ProductStatusChanged changed:
                _statusChanges.Add(
                    1,
                    Context,
                    new KeyValuePair<string, object?>(
                        "catalog.product.status",
                        changed.To.ToString().ToLowerInvariant()
                    )
                );
                break;
            case ProductPriceChanged:
                _priceChanges.Add(1, Context);
                break;
            case ProductDeleted:
                _deleted.Add(1, Context);
                break;
        }

        return Task.CompletedTask;
    }
}
