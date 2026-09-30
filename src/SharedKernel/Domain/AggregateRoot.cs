namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base class for aggregate roots. Adds optimistic-concurrency versioning
/// and domain-event collection to <see cref="Entity"/>.
/// </summary>
internal abstract class AggregateRoot : Entity
{
    /// <summary>Version of a freshly created aggregate.</summary>
    public const int InitialVersion = 1;

    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Concurrency token: <see cref="InitialVersion"/> on creation and incremented exactly once per
    /// state change, whether or not the change raises an event. Mapped to the mandatory <c>version</c> column.
    /// </summary>
    public int Version { get; protected set; }

    /// <summary>Raised but not yet published domain events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    protected AggregateRoot() { }

    /// <summary>
    /// Initializes a new aggregate root.
    /// </summary>
    /// <param name="id">Aggregate identifier. Must not be empty.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected AggregateRoot(Guid id, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        Version = InitialVersion;
    }

    /// <summary>Clears unpublished domain events after persistence.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Records a domain event. Does not touch the version: raise the event after the state change
    /// so that <see cref="Version"/> already reflects it.
    /// </summary>
    /// <param name="domainEvent">The event that occurred.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainEvent"/> is null.</exception>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <inheritdoc />
    protected override void OnModified() => Version++;
}
