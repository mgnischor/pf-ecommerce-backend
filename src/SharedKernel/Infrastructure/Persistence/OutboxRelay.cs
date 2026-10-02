using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Publishes the outbox of one context to the broker, at least once (ai/DATABASE.md §3.1). A batch is claimed
/// with <c>SELECT … FOR UPDATE SKIP LOCKED</c> in a short transaction that only stamps a lease on the rows, so any
/// number of instances can run side by side and no transaction stays open across the broker call. A row whose
/// publisher failed, or whose instance died, becomes claimable again when its lease expires.
/// Ordering is per poll, not guaranteed across instances: consumers use <see cref="OutboxMessage.AggregateVersion"/>.
/// Each publication is a <c>PRODUCER</c> span that continues the trace stored with the event (ai/OBSERVABILITY.md §6.4), not
/// one started by the polling loop, so a request and the message it caused are one trace.
/// </summary>
/// <typeparam name="TContext">The context whose outbox is relayed.</typeparam>
internal sealed class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    IOutboxPublisher publisher,
    TimeProvider timeProvider,
    IOptions<OutboxRelayOptions> options,
    IMeterFactory meterFactory,
    ILogger<OutboxRelay<TContext>> logger
) : BackgroundService
    where TContext : ModuleDbContext
{
    private readonly Histogram<double> _publishDuration = meterFactory
        .Create(TelemetryNames.OutboxMeter)
        .CreateHistogram<double>(
            "app.outbox.publish.duration",
            unit: "s",
            description: "Duration of one outbox publication, by outcome (success or failure)."
        );

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
            using var activity = StartPublishActivity(message, context.Schema);
            var started = Stopwatch.GetTimestamp();
            var outcome = "success";

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
                outcome = "failure";
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.type", exception.GetType().Name);
                message.MarkFailed(exception.GetType().Name);
                OutboxLog.PublishFailed(logger, typeof(TContext).Name, message.Attempts, exception.GetType().Name);
            }
            finally
            {
                _publishDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalSeconds,
                    new KeyValuePair<string, object?>(TelemetryNames.BoundedContext, context.Schema),
                    new KeyValuePair<string, object?>("app.outcome", outcome)
                );
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return claimed.Count;
    }

    private static Activity? StartPublishActivity(OutboxMessage message, string schema)
    {
        // Continue the trace of the request that raised the event; without a stored context this is a new root.
        var parent = default(ActivityContext);
        if (
            message.TraceParent is not null
            && !ActivityContext.TryParse(message.TraceParent, message.TraceState, isRemote: true, out parent)
        )
        {
            parent = default; // A malformed stored context is ignored: the span starts a new trace.
        }

        var activity = OutboxTracing.Source.StartActivity(
            $"publish {message.Type[(message.Type.LastIndexOf('.') + 1)..]}",
            ActivityKind.Producer,
            parent
        );
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.message.id", message.Id.ToString("D"));
        activity?.SetTag(TelemetryNames.BoundedContext, schema);
        activity?.SetTag("app.outbox.attempt", message.Attempts);
        return activity;
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
                           correlation_id, causation_id, trace_parent, trace_state,
                           processed_at, attempts, locked_until, last_error
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
