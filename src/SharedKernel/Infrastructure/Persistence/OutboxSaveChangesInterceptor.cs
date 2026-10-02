using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The transactional outbox (ai/DATABASE.md §3.1): converts the domain events raised by the tracked aggregates
/// into <see cref="OutboxMessage"/> rows that commit in the same transaction as the state change, so an event
/// exists if and only if its change was committed. Nothing is ever published from here; the relay does that
/// after the commit. The row stores the trace context of the request that raised the event (ai/OBSERVABILITY.md §6.4), so
/// the relay can continue the trace. The events are cleared from the aggregates, and handed to the
/// <see cref="IDomainEventSubscriber"/>s, only once the commit succeeded.
/// </summary>
internal sealed partial class OutboxSaveChangesInterceptor(
    IEnumerable<IDomainEventSubscriber> subscribers,
    ILogger<OutboxSaveChangesInterceptor> logger
) : SaveChangesInterceptor
{
    private readonly IDomainEventSubscriber[] _subscribers = [.. subscribers];

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Enqueue(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Enqueue(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var committed = TakeEvents(eventData.Context);
        // The synchronous path is not used by the use cases; it exists because EF Core calls it from SaveChanges().
        DispatchAsync(committed, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await DispatchAsync(TakeEvents(eventData.Context), cancellationToken);
        return result;
    }

    private static void Enqueue(DbContext? context)
    {
        if (context is not ModuleDbContext module)
        {
            return;
        }

        var current = Activity.Current;
        var correlationId = current?.TraceId.ToString();
        var traceParent = current?.IdFormat == ActivityIdFormat.W3C ? current.Id : null;
        var traceState = current?.TraceStateString;

        foreach (var entry in module.ChangeTracker.Entries<AggregateRoot>().ToList())
        {
            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                // A failed commit that is retried by the caller finds its rows still tracked: queue each event once.
                if (module.OutboxMessages.Local.Any(message => message.Id == domainEvent.EventId))
                {
                    continue;
                }

                module.OutboxMessages.Add(
                    OutboxMessage.From(domainEvent, correlationId, causationId: null, traceParent, traceState)
                );
            }
        }
    }

    private static List<IDomainEvent> TakeEvents(DbContext? context)
    {
        var committed = new List<IDomainEvent>();
        if (context is null)
        {
            return committed;
        }

        foreach (var aggregate in context.ChangeTracker.Entries<AggregateRoot>().Select(entry => entry.Entity))
        {
            committed.AddRange(aggregate.DomainEvents);
            aggregate.ClearDomainEvents();
        }

        return committed;
    }

    private async Task DispatchAsync(List<IDomainEvent> committed, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in committed)
        {
            foreach (var subscriber in _subscribers)
            {
                try
                {
                    await subscriber.OnCommittedAsync(domainEvent, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The change is already committed: a subscriber must never turn that into a failed request.
                    SubscriberLog.Failed(
                        logger,
                        subscriber.GetType().Name,
                        domainEvent.GetType().Name,
                        exception.GetType().Name
                    );
                }
            }
        }
    }
}
