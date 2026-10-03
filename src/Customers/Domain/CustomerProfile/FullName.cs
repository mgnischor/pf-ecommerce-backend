using System.Text;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// Name of a customer as they entered it (BR-CUS-001). Immutable, compared by value. No assumption is made about the
/// structure of a name (ai/API_CONTRACTS.md §7): it is normalized to Unicode NFC, trimmed, and every run of whitespace
/// becomes one space; it has 2 to 120 characters, no control character, and at least one letter.
/// </summary>
internal sealed record FullName
{
    /// <summary>Minimum length, in characters.</summary>
    public const int MinLength = 2;

    /// <summary>Maximum length, in characters.</summary>
    public const int MaxLength = 120;

    /// <summary>Normalized name.</summary>
    public string Value { get; }

    private FullName(string value)
    {
        Value = value;
    }

    /// <summary>Validates and normalizes a raw name.</summary>
    /// <param name="value">Raw name.</param>
    /// <returns>The normalized name, or the violated BR-CUS-001 error.</returns>
    public static Result<FullName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<FullName>.Failure(CustomerErrors.FullNameRequired);
        }

        var normalized = CollapseWhitespace(value.Normalize(NormalizationForm.FormC));

        if (normalized.Length is < MinLength or > MaxLength)
        {
            return Result<FullName>.Failure(CustomerErrors.FullNameLength);
        }

        return normalized.Any(char.IsControl) || !normalized.Any(char.IsLetter)
            ? Result<FullName>.Failure(CustomerErrors.FullNameInvalid)
            : Result<FullName>.Success(new FullName(normalized));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) && !char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
