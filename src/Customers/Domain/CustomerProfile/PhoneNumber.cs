using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// Phone of a customer as an E.164 number (BR-CUS-003): <c>+</c>, then 8 to 15 digits, the first not <c>0</c>. The API
/// exchanges machine values and leaves presentation to the client (ai/API_CONTRACTS.md §7), so spaces, hyphens and
/// parentheses are rejected rather than stripped. Immutable, compared by value. Shown only masked (BR-CUS-009).
/// </summary>
internal sealed record PhoneNumber
{
    /// <summary>Fewest digits after the <c>+</c>.</summary>
    public const int MinDigits = 8;

    /// <summary>Most digits after the <c>+</c> (ITU-T E.164).</summary>
    public const int MaxDigits = 15;

    /// <summary>Digits of the number that stay visible when masked.</summary>
    public const int VisibleDigits = 4;

    /// <summary>The number, <c>+</c> included.</summary>
    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    /// <summary>Validates a raw number (surrounding whitespace is ignored).</summary>
    /// <param name="value">Raw number.</param>
    /// <returns>The number, or <c>CUSTOMER_PHONE_INVALID</c>.</returns>
    public static Result<PhoneNumber> Create(string? value)
    {
        var candidate = value?.Trim();

        return candidate is not null && IsE164(candidate)
            ? Result<PhoneNumber>.Success(new PhoneNumber(candidate))
            : Result<PhoneNumber>.Failure(CustomerErrors.PhoneInvalid);
    }

    /// <summary>
    /// The number with every digit but the last <see cref="VisibleDigits"/> replaced by <c>*</c>
    /// (<c>+*********4321</c>), keeping the length so the customer can still recognize it (BR-CUS-009).
    /// </summary>
    public string Masked() =>
        string.Concat(
            "+",
            new string('*', Value.Length - 1 - VisibleDigits),
            Value.AsSpan(Value.Length - VisibleDigits)
        );

    /// <inheritdoc />
    public override string ToString() => Masked();

    private static bool IsE164(string candidate) =>
        candidate.Length is >= MinDigits + 1 and <= MaxDigits + 1
        && candidate[0] == '+'
        && candidate[1] is >= '1' and <= '9'
        && candidate.Skip(1).All(char.IsAsciiDigit);
}
