using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// Language and region a customer prefers for the text the platform produces for them (BR-CUS-004). Only the
/// supported set is accepted (<c>pt-BR</c> and <c>en</c> are the initial targets of ai/API_CONTRACTS.md §7, to be
/// confirmed by ADR); input is matched case-insensitively and stored in its canonical casing. Immutable.
/// </summary>
internal sealed record CustomerLocale
{
    /// <summary>The supported locales, in canonical casing.</summary>
    public static readonly IReadOnlyList<string> Supported = ["pt-BR", "en"];

    /// <summary>Canonical locale tag.</summary>
    public string Value { get; }

    private CustomerLocale(string value)
    {
        Value = value;
    }

    /// <summary>Locale of a customer who gave none.</summary>
    public static CustomerLocale Default { get; } = new(Supported[0]);

    /// <summary>Validates a raw locale.</summary>
    /// <param name="value">Raw locale tag.</param>
    /// <returns>The canonical locale, or the violated BR-CUS-004 error.</returns>
    public static Result<CustomerLocale> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<CustomerLocale>.Failure(CustomerErrors.LocaleRequired);
        }

        var canonical = Supported.FirstOrDefault(tag =>
            string.Equals(tag, value.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        return canonical is null
            ? Result<CustomerLocale>.Failure(CustomerErrors.LocaleUnsupported)
            : Result<CustomerLocale>.Success(new CustomerLocale(canonical));
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
