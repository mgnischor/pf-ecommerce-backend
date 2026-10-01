using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Keeps the traceability columns true on every commit (ai/DATABASE.md §2.2, §7.1), using the injected
/// <see cref="TimeProvider"/>: stamps the creation and update instants, and turns the deletion of an entity
/// into a logical deletion (<c>deleted_at</c>) so history is kept and a row is never physically removed.
/// </summary>
internal sealed class AuditingSaveChangesInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);
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

        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<Entity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfDefault(entry, nameof(Entity.CreatedAt), now);
                    SetIfDefault(entry, nameof(Entity.UpdatedAt), now);
                    break;
                case EntityState.Modified:
                    Touch(entry, now);
                    break;
                case EntityState.Deleted:
                    SoftDelete(entry, now);
                    break;
            }
        }
    }

    private static void SetIfDefault(EntityEntry<Entity> entry, string property, DateTimeOffset now)
    {
        var member = entry.Property(property);
        if ((DateTimeOffset)member.CurrentValue! == default)
        {
            member.CurrentValue = now;
        }
    }

    private static void Touch(EntityEntry<Entity> entry, DateTimeOffset now)
    {
        // Never move the timestamp backwards: the domain already stamped it with the same clock.
        var updatedAt = entry.Property(nameof(Entity.UpdatedAt));
        if ((DateTimeOffset)updatedAt.CurrentValue! < now)
        {
            updatedAt.CurrentValue = now;
        }
    }

    private static void SoftDelete(EntityEntry<Entity> entry, DateTimeOffset now)
    {
        // Back to Unchanged keeps the original values, including the version the concurrency check compares.
        entry.State = EntityState.Unchanged;
        entry.Property(nameof(Entity.DeletedAt)).CurrentValue = now;
        entry.Property(nameof(Entity.UpdatedAt)).CurrentValue = now;

        if (entry.Entity is AggregateRoot)
        {
            var version = entry.Property(nameof(AggregateRoot.Version));
            version.CurrentValue = (int)version.CurrentValue! + 1;
        }
    }
}
