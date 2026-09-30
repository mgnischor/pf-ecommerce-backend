namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base class for all persisted domain entities.
/// Provides identity plus mandatory traceability fields
/// (<c>Id</c>, <c>CreatedAt</c>, <c>UpdatedAt</c>, <c>DeletedAt</c>, UTC).
/// Two entities are equal when they have the same runtime type and the same non-empty identity.
/// </summary>
internal abstract class Entity
{
    /// <summary>Unique identity of the entity. Generated in the domain.</summary>
    public Guid Id { get; protected set; }

    /// <summary>UTC timestamp of creation.</summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>UTC timestamp of the logical deletion, or <c>null</c> while active.</summary>
    public DateTimeOffset? DeletedAt { get; protected set; }

    /// <summary>Whether the entity is logically deleted.</summary>
    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    protected Entity() { }

    /// <summary>
    /// Initializes a new entity with domain-generated identity and timestamps from <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="id">Entity identifier. Must not be empty.</param>
    /// <param name="timeProvider">Source of UTC time. Never read the clock directly.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    protected Entity(Guid id, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }

        Id = id;
        var now = timeProvider.GetUtcNow();
        CreatedAt = now;
        UpdatedAt = now;
        DeletedAt = null;
    }

    /// <summary>Creates a time-ordered, index-friendly identifier stamped with the injected clock.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns>A new version 7 UUID.</returns>
    public static Guid NewId(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return Guid.CreateVersion7(timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Called after every state change (update, deletion, restoration).
    /// Aggregate roots use it to advance their concurrency version.
    /// </summary>
    protected virtual void OnModified() { }

    /// <summary>Refreshes the update timestamp.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void MarkUpdated(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        UpdatedAt = timeProvider.GetUtcNow();
        OnModified();
    }

    /// <summary>Performs a logical deletion, preserving history.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void MarkDeleted(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();
        DeletedAt = now;
        UpdatedAt = now;
        OnModified();
    }

    /// <summary>Restores a logically deleted entity.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void Restore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        DeletedAt = null;
        UpdatedAt = timeProvider.GetUtcNow();
        OnModified();
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj is Entity other && GetType() == other.GetType() && Id != Guid.Empty && Id == other.Id;
    }

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
