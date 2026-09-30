namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Immutable record of something that happened in the domain.
/// Carries the mandatory metadata envelope: event ID, aggregate ID and version, UTC timestamp.
/// Integration events additionally carry correlation and causation IDs.
/// </summary>
internal interface IDomainEvent
{
    /// <summary>Unique identifier of this event occurrence.</summary>
    Guid EventId { get; }

    /// <summary>Identifier of the aggregate that raised the event.</summary>
    Guid AggregateId { get; }

    /// <summary>
    /// Version of the aggregate <em>after</em> the change that raised the event.
    /// Lets consumers detect gaps and out-of-order delivery.
    /// </summary>
    int AggregateVersion { get; }

    /// <summary>UTC timestamp of when the event occurred.</summary>
    DateTimeOffset OccurredAt { get; }
}
