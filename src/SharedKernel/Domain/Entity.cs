namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base class for all persisted domain entities.
/// Provides identity plus mandatory traceability fields
/// (<c>Id</c>, <c>CreatedAt</c>, <c>UpdatedAt</c>, <c>DeletedAt</c>, UTC).
/// </summary>
public abstract class Entity : IEquatable<Entity>
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
    protected Entity()
    {
    }

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

    /// <summary>Creates a time-ordered, index-friendly identifier.</summary>
    /// <returns>A new version 7 UUID.</returns>
    public static Guid NewId() => Guid.CreateVersion7();

    /// <summary>Refreshes the update timestamp.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void MarkUpdated(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Performs a logical deletion, preserving history.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void MarkDeleted(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();
        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Restores a logically deleted entity.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    protected void Restore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        DeletedAt = null;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Id != Guid.Empty && Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Entity);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>Identity equality operator.</summary>
    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    /// <summary>Identity inequality operator.</summary>
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
