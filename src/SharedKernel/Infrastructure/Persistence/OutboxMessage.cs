using System.Text.Json;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// One row of a context's transactional outbox (ai/DATABASE.md §3.1): a domain event written in the same
/// transaction as the state change that raised it, published later by <see cref="OutboxRelay{TContext}"/>.
/// A technical table, not a domain entity: it has no soft delete and its own lifecycle columns.
/// </summary>
internal sealed class OutboxMessage
{
    /// <summary>Longest stored failure description.</summary>
    public const int MaxErrorLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The event identifier: the primary key, so one event can never be queued twice.</summary>
    public Guid Id { get; private set; }

    /// <summary>Full name of the event type, for the publisher to route and consumers to deserialize.</summary>
    public string Type { get; private set; }

    /// <summary>The event as camelCase JSON (<c>jsonb</c>).</summary>
    public string Payload { get; private set; }

    /// <summary>Aggregate that raised the event.</summary>
    public Guid AggregateId { get; private set; }

    /// <summary>Version of the aggregate after the change; lets consumers detect gaps and reordering.</summary>
    public int AggregateVersion { get; private set; }

    /// <summary>UTC instant the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Trace identifier of the request that caused the event, when there was one.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Identifier of the message that caused this event, for events raised while handling another message.</summary>
    public string? CausationId { get; private set; }

    /// <summary>UTC instant the publisher confirmed the event, or <c>null</c> while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Publication attempts so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>UTC end of the lease of the relay instance that claimed the row, or <c>null</c>.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Exception type of the last failed attempt. Never a message: those may carry payload data.</summary>
    public string? LastError { get; private set; }

    /// <summary>EF Core constructor. Do not use in application code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private OutboxMessage() { }
#pragma warning restore CS8618

    /// <summary>Queues a domain event.</summary>
    /// <param name="domainEvent">The event; serialized by its runtime type.</param>
    /// <param name="correlationId">Trace identifier of the current request, if any.</param>
    /// <param name="causationId">Identifier of the message being handled, if any.</param>
    public static OutboxMessage From(IDomainEvent domainEvent, string? correlationId, string? causationId)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Id = domainEvent.EventId,
            Type = type.FullName ?? type.Name,
            Payload = JsonSerializer.Serialize(domainEvent, type, Json),
            AggregateId = domainEvent.AggregateId,
            AggregateVersion = domainEvent.AggregateVersion,
            OccurredAt = domainEvent.OccurredAt,
            CorrelationId = correlationId,
            CausationId = causationId,
        };
    }

    /// <summary>Claims the row for one relay instance until <paramref name="until"/> and counts the attempt.</summary>
    /// <param name="until">UTC end of the lease.</param>
    public void Lease(DateTimeOffset until)
    {
        LockedUntil = until;
        Attempts++;
    }

    /// <summary>Records that the publisher confirmed the event.</summary>
    /// <param name="now">Current UTC instant.</param>
    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>Records a failed attempt and frees the row for the next lease.</summary>
    /// <param name="errorType">Name of the exception type.</param>
    public void MarkFailed(string errorType)
    {
        LastError = errorType.Length > MaxErrorLength ? errorType[..MaxErrorLength] : errorType;
        LockedUntil = null;
    }
}
