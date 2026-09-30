using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Stock-keeping unit identifying a product. Immutable, compared by value.
/// Normalized to uppercase; 4 to 32 characters of letters, digits, '-' or '_'.
/// </summary>
public sealed class Sku : ValueObject
{
    /// <summary>Minimum length of a SKU code.</summary>
    public const int MinLength = 4;

    /// <summary>Maximum length of a SKU code.</summary>
    public const int MaxLength = 32;

    /// <summary>Normalized SKU code.</summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new SKU, normalizing <paramref name="value"/> to uppercase.
    /// </summary>
    /// <param name="value">Raw SKU code.</param>
    /// <exception cref="ArgumentException">Thrown when the code is empty, has an invalid length, or contains invalid characters.</exception>
    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SKU must not be empty.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length < MinLength || normalized.Length > MaxLength)
        {
            throw new ArgumentException(
                $"SKU must be between {MinLength} and {MaxLength} characters.", nameof(value));
        }

        foreach (var c in normalized)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                throw new ArgumentException(
                    "SKU may only contain letters, digits, '-' and '_'.", nameof(value));
            }
        }

        Value = normalized;
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
