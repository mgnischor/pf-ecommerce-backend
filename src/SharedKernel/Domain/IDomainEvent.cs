namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Immutable record of something that happened in the domain.
/// Carries the mandatory metadata envelope: event ID, aggregate ID, UTC timestamp.
/// Integration events additionally carry correlation and causation IDs.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identifier of this event occurrence.</summary>
    Guid EventId { get; }

    /// <summary>Identifier of the aggregate that raised the event.</summary>
    Guid AggregateId { get; }

    /// <summary>UTC timestamp of when the event occurred.</summary>
    DateTimeOffset OccurredAt { get; }
}
