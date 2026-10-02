using System.Diagnostics.Metrics;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Business KPIs of the Identity context, derived from its domain events after the commit (ai/OBSERVABILITY.md §3.3, §5.5).
/// Counts only: never an e-mail, a name or an account id (ai/OBSERVABILITY.md §5.4, ai/SECURITY.md §11.2). Sign-in outcomes,
/// which are not domain events, are counted by <see cref="SecurityAuditLog"/> in the same meter.
/// </summary>
internal sealed class IdentityMetricsSubscriber : IDomainEventSubscriber
{
    private static readonly KeyValuePair<string, object?> Context = new(TelemetryNames.BoundedContext, "identity");

    private readonly Counter<long> _registered;
    private readonly Counter<long> _deactivated;
    private readonly Counter<long> _levelChanges;

    /// <summary>Creates the subscriber and its instruments.</summary>
    /// <param name="meterFactory">Creates the meter, disposed with the host.</param>
    public IdentityMetricsSubscriber(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.ContextMeter("Identity"));
        _registered = meter.CreateCounter<long>(
            "identity.accounts.registered",
            unit: "{account}",
            description: "Accounts created, by the access level they were created with."
        );
        _deactivated = meter.CreateCounter<long>(
            "identity.accounts.deactivated",
            unit: "{account}",
            description: "Accounts deactivated."
        );
        _levelChanges = meter.CreateCounter<long>(
            "identity.access_level.changes",
            unit: "{change}",
            description: "Access-level changes, by the level reached (a privilege change)."
        );
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case UserRegistered registered:
                _registered.Add(1, Context, Level(registered.Level));
                break;
            case UserDeactivated:
                _deactivated.Add(1, Context);
                break;
            case UserAccessLevelChanged changed:
                _levelChanges.Add(1, Context, Level(changed.To));
                break;
        }

        return Task.CompletedTask;
    }

    private static KeyValuePair<string, object?> Level(AccessLevel level) =>
        new("identity.access_level", level.ToString().ToLowerInvariant());
}
