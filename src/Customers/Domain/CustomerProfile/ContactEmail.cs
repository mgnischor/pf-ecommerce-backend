using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// E-mail address of a customer (BR-CUS-002), copied from the account when the profile is created. The account's own
/// rules (BR-IDN-001) already guarantee a well-formed address; this type restates only what Customers relies on,
/// because a context never references another context's types. Immutable, compared by value, lowercase.
/// It is shown to the customer only masked (BR-CUS-009).
/// </summary>
internal sealed record ContactEmail
{
    /// <summary>Maximum total length (RFC 5321).</summary>
    public const int MaxLength = 254;

    /// <summary>Normalized address.</summary>
    public string Value { get; }

    private ContactEmail(string value)
    {
        Value = value;
    }

    /// <summary>Validates and normalizes a raw address.</summary>
    /// <param name="value">Raw address.</param>
    /// <returns>The normalized address, or <c>CUSTOMER_EMAIL_INVALID</c>.</returns>
    public static Result<ContactEmail> Create(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized is not null && IsWellFormed(normalized)
            ? Result<ContactEmail>.Success(new ContactEmail(normalized))
            : Result<ContactEmail>.Failure(CustomerErrors.EmailInvalid);
    }

    /// <summary>
    /// The address with the local part reduced to its first character (<c>a***@example.com</c>), so the customer
    /// recognizes it and a leaked response reveals neither the name nor the length of the local part (BR-CUS-009).
    /// </summary>
    public string Masked()
    {
        var at = Value.IndexOf('@', StringComparison.Ordinal);
        return string.Concat(Value.AsSpan(0, 1), "***", Value.AsSpan(at));
    }

    /// <inheritdoc />
    public override string ToString() => Masked();

    private static bool IsWellFormed(string candidate)
    {
        if (candidate.Length is 0 or > MaxLength || candidate.Any(c => c <= ' ' || c >= (char)127))
        {
            return false;
        }

        var at = candidate.IndexOf('@', StringComparison.Ordinal);

        return at >= 1
            && at == candidate.LastIndexOf('@')
            && at < candidate.Length - 3
            && candidate[(at + 1)..].Contains('.', StringComparison.Ordinal);
    }
}
