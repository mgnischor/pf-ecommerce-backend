namespace Portfolio.SharedKernel.Domain;

/// <summary>Stable errors raised while building <see cref="Money"/>.</summary>
internal static class MoneyErrors
{
    /// <summary>The currency is not a 3-letter ISO 4217 code.</summary>
    public static Error InvalidCurrency => Error.Validation("MONEY_INVALID_CURRENCY", "currency");
}
