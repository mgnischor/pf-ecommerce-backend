using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Reacts to a domain event <b>after its transaction committed</b> (ai/OBSERVABILITY.md §3.3, ai/DATABASE.md §4.2): business
/// metrics and cache invalidation are derived from events here, in Infrastructure, never inside the aggregate. Running after
/// the commit means work that was rolled back is never counted and never invalidates anything. A subscriber failure is
/// logged and swallowed: the state change is already durable, and the outbox still carries the event.
/// </summary>
internal interface IDomainEventSubscriber
{
    /// <summary>Handles one committed event. Must be quick and must not throw for expected conditions.</summary>
    /// <param name="domainEvent">The committed event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnCommittedAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}
