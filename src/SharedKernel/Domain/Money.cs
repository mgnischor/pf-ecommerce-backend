using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Monetary amount with ISO 4217 currency. Immutable, compared by value, <c>decimal</c> only.
/// </summary>
/// <remarks>
/// Rounding policy (centralized here, ai/BUSINESS.md §7.1): every amount is rounded to
/// <see cref="MaxScale"/> decimal places using <see cref="MidpointRounding.ToEven"/> on construction,
/// so every operation result is already normalized.
/// </remarks>
internal sealed record Money
{
    /// <summary>Scale of the stored amount; matches the <c>numeric(19,4)</c> column type.</summary>
    public const int MaxScale = 4;

    /// <summary>Length of an ISO 4217 currency code.</summary>
    public const int CurrencyLength = 3;

    /// <summary>Monetary amount.</summary>
    public decimal Amount { get; }

    /// <summary>ISO 4217 currency code (e.g. BRL, USD). Always uppercase.</summary>
    public string Currency { get; }

    /// <summary>Whether the amount is greater than zero.</summary>
    public bool IsPositive => Amount > 0;

    /// <summary>
    /// Initializes a new money value.
    /// </summary>
    /// <param name="amount">Monetary amount.</param>
    /// <param name="currency">ISO 4217 currency code (3 ASCII letters).</param>
    /// <exception cref="ArgumentException">Thrown when the currency is invalid. Use <see cref="Create"/> for untrusted input.</exception>
    public Money(decimal amount, string currency)
    {
        if (!IsValidCurrency(currency))
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        Amount = decimal.Round(amount, MaxScale, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>Builds a money value from untrusted input, reporting an invalid currency as a failure.</summary>
    /// <param name="amount">Monetary amount.</param>
    /// <param name="currency">Candidate ISO 4217 currency code.</param>
    public static Result<Money> Create(decimal amount, string? currency) =>
        IsValidCurrency(currency)
            ? Result<Money>.Success(new Money(amount, currency))
            : Result<Money>.Failure(MoneyErrors.InvalidCurrency);

    /// <summary>Creates a zero amount in the given currency.</summary>
    /// <param name="currency">ISO 4217 currency code.</param>
    public static Money Zero(string currency) => new(0m, currency);

    /// <summary>Adds two amounts in the same currency.</summary>
    /// <param name="other">Amount to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when currencies differ.</exception>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts an amount in the same currency.</summary>
    /// <param name="other">Amount to subtract.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when currencies differ.</exception>
    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Multiplies the amount by a whole quantity (e.g. unit price × quantity).</summary>
    /// <param name="quantity">Non-negative multiplier.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="quantity"/> is negative.</exception>
    public Money Multiply(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        return new Money(Amount * quantity, Currency);
    }

    /// <summary>Formats as <c>1234.50 BRL</c> using the invariant culture.</summary>
    public override string ToString() => $"{Amount.ToString("F2", CultureInfo.InvariantCulture)} {Currency}";

    private static bool IsValidCurrency([NotNullWhen(true)] string? currency) =>
        currency is { Length: CurrencyLength } && currency.All(char.IsAsciiLetter);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot operate on Money with different currencies: {Currency} and {other.Currency}."
            );
        }
    }
}
