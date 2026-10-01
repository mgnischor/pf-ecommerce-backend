using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// Stock-keeping unit that identifies an inventory item (BR-INV-007). Immutable, compared by value.
/// The format is the Catalog's published language (BR-CAT-004), restated here because a context never
/// references another context's types: normalized to uppercase, 4 to 32 ASCII letters, digits, '-' or '_'.
/// </summary>
internal sealed record Sku
{
    /// <summary>Minimum length of a SKU code.</summary>
    public const int MinLength = 4;

    /// <summary>Maximum length of a SKU code.</summary>
    public const int MaxLength = 32;

    /// <summary>Normalized SKU code.</summary>
    public string Value { get; }

    private Sku(string value)
    {
        Value = value;
    }

    /// <summary>Validates and normalizes a raw SKU code. ASCII-only so look-alike codes cannot split one stock.</summary>
    /// <param name="value">Raw SKU code.</param>
    /// <returns>The normalized SKU, or the violated BR-INV-007 error.</returns>
    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Sku>.Failure(InventoryErrors.SkuRequired);
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length is < MinLength or > MaxLength)
        {
            return Result<Sku>.Failure(InventoryErrors.SkuLength);
        }

        return normalized.All(IsAllowedCharacter)
            ? Result<Sku>.Success(new Sku(normalized))
            : Result<Sku>.Failure(InventoryErrors.SkuInvalidCharacters);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsAllowedCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
