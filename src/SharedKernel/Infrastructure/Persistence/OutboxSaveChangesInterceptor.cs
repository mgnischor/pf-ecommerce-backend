using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The transactional outbox (ai/DATABASE.md §3.1): converts the domain events raised by the tracked aggregates
/// into <see cref="OutboxMessage"/> rows that commit in the same transaction as the state change, so an event
/// exists if and only if its change was committed. Nothing is ever published from here; the relay does that
/// after the commit. The events are cleared from the aggregates only once the commit succeeded.
/// </summary>
internal sealed class OutboxSaveChangesInterceptor : SaveChangesInterceptor
{
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

        ClearEvents(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        ClearEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Enqueue(DbContext? context)
    {
        if (context is not ModuleDbContext module)
        {
            return;
        }

        var correlationId = Activity.Current?.TraceId.ToString();

        foreach (var entry in module.ChangeTracker.Entries<AggregateRoot>().ToList())
        {
            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                // A failed commit that is retried by the caller finds its rows still tracked: queue each event once.
                if (module.OutboxMessages.Local.Any(message => message.Id == domainEvent.EventId))
                {
                    continue;
                }

                module.OutboxMessages.Add(OutboxMessage.From(domainEvent, correlationId, causationId: null));
            }
        }
    }

    private static void ClearEvents(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AggregateRoot>())
        {
            entry.Entity.ClearDomainEvents();
        }
    }
}
