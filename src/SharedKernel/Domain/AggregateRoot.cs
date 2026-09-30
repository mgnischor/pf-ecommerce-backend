namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base class for aggregate roots. Adds optimistic-concurrency versioning
/// and domain-event collection to <see cref="Entity"/>.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Concurrency token, incremented on every state change.
    /// Mapped to the mandatory <c>version</c> column.
    /// </summary>
    public int Version { get; protected set; }

    /// <summary>Raised but not yet published domain events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Initializes a new aggregate root.
    /// </summary>
    /// <param name="id">Aggregate identifier. Must not be empty.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected AggregateRoot(Guid id, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
    }

    /// <summary>
    /// Records a domain event and advances the concurrency version.
    /// </summary>
    /// <param name="domainEvent">The event that occurred.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is null.</exception>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
        Version++;
    }

    /// <summary>Clears unpublished domain events after persistence.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
