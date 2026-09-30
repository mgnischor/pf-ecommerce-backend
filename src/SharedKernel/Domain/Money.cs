namespace Portfolio.SharedKernel.Domain;

/// <summary>
/// Monetary amount with ISO 4217 currency. Immutable. Uses <c>decimal</c> only.
/// </summary>
public sealed class Money : ValueObject
{
    /// <summary>Maximum scale accepted for monetary amounts.</summary>
    public const int MaxScale = 4;

    /// <summary>Monetary amount.</summary>
    public decimal Amount { get; }

    /// <summary>ISO 4217 currency code (e.g. BRL, USD). Always uppercase.</summary>
    public string Currency { get; }

    /// <summary>
    /// Initializes a new money value.
    /// </summary>
    /// <param name="amount">Monetary amount.</param>
    /// <param name="currency">ISO 4217 currency code (3 letters).</param>
    /// <exception cref="ArgumentException">Thrown when the currency is invalid.</exception>
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        Amount = decimal.Round(amount, MaxScale, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

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

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot operate on Money with different currencies: {Currency} and {other.Currency}.");
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Amount:F2} {Currency}";
}
