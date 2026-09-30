namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Base class for immutable value objects, compared by value instead of identity.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>Returns the components that define equality.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public bool Equals(ValueObject? other)
    {
        if (other is null || GetType() != other.GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>Value equality operator.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    /// <summary>Value inequality operator.</summary>
    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
