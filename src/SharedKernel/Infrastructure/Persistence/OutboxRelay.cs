using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Publishes the outbox of one context to the broker, at least once (ai/DATABASE.md §3.1). A batch is claimed
/// with <c>SELECT … FOR UPDATE SKIP LOCKED</c> in a short transaction that only stamps a lease on the rows, so any
/// number of instances can run side by side and no transaction stays open across the broker call. A row whose
/// publisher failed, or whose instance died, becomes claimable again when its lease expires.
/// Ordering is per poll, not guaranteed across instances: consumers use <see cref="OutboxMessage.AggregateVersion"/>.
/// </summary>
/// <typeparam name="TContext">The context whose outbox is relayed.</typeparam>
internal sealed class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    IOutboxPublisher publisher,
    TimeProvider timeProvider,
    IOptions<OutboxRelayOptions> options,
    ILogger<OutboxRelay<TContext>> logger
) : BackgroundService
    where TContext : ModuleDbContext
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var relayed = 0;
            try
            {
                relayed = await RelayBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
                when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
            {
                // The database is unreachable or the batch could not be recorded: retry at the next poll.
                OutboxLog.BatchFailed(logger, typeof(TContext).Name, exception.GetType().Name);
            }

            if (relayed == 0)
            {
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>Claims one batch, publishes it, and records the outcome of each message.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many messages were claimed.</returns>
    public async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var claimed = await ClaimAsync(context, cancellationToken);

        foreach (var message in claimed)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.MarkProcessed(timeProvider.GetUtcNow());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Any publisher failure keeps the message pending; only the type is stored and logged.
                message.MarkFailed(exception.GetType().Name);
                OutboxLog.PublishFailed(logger, typeof(TContext).Name, message.Attempts, exception.GetType().Name);
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return claimed.Count;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(TContext context, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var now = timeProvider.GetUtcNow();
            var schema = context.Schema;

            // The schema is a constant of the context, but identifiers cannot be parameters: scope the lookup with
            // a parameterized search_path for this transaction and keep the statement itself free of concatenation.
            await context.Database.ExecuteSqlAsync(
                $"SELECT set_config('search_path', {schema}, true)",
                cancellationToken
            );

            var batch = await context
                .OutboxMessages.FromSql(
                    $"""
                    SELECT id, type, payload, aggregate_id, aggregate_version, occurred_at,
                           correlation_id, causation_id, processed_at, attempts, locked_until, last_error
                    FROM outbox_messages
                    WHERE processed_at IS NULL
                      AND attempts < {settings.MaxAttempts}
                      AND (locked_until IS NULL OR locked_until < {now})
                    ORDER BY occurred_at
                    LIMIT {settings.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """
                )
                .ToListAsync(cancellationToken);

            foreach (var message in batch)
            {
                message.Lease(now + settings.Lease);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return batch;
        });
    }
}
