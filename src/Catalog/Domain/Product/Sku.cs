using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Stock-keeping unit identifying a product (BR-CAT-004). Immutable, compared by value.
/// Normalized to uppercase; 4 to 32 ASCII letters, digits, '-' or '_'.
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

    /// <summary>
    /// Validates and normalizes a raw SKU code. ASCII-only on purpose: Unicode letters would allow
    /// visually identical but distinct codes that defeat BR-CAT-005 (uniqueness).
    /// </summary>
    /// <param name="value">Raw SKU code.</param>
    /// <returns>The normalized SKU, or the violated BR-CAT-004 error.</returns>
    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Sku>.Failure(ProductErrors.SkuRequired);
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length is < MinLength or > MaxLength)
        {
            return Result<Sku>.Failure(ProductErrors.SkuLength);
        }

        return normalized.All(IsAllowedCharacter)
            ? Result<Sku>.Success(new Sku(normalized))
            : Result<Sku>.Failure(ProductErrors.SkuInvalidCharacters);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsAllowedCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
