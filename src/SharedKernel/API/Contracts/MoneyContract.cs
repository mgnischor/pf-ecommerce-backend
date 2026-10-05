using System.Globalization;

namespace Portfolio.SharedKernel.API.Contracts;

/// <summary>Converts monetary values between their wire form (a decimal string, never a JSON number) and <c>decimal</c>.</summary>
internal static class MoneyContract
{
    /// <summary>Format of an amount on the wire: at least two and at most four fraction digits (<c>"25.90"</c>).</summary>
    private const string AmountFormat = "0.00##";

    /// <summary>
    /// Reads the amount of a request. The request is validated against <see cref="ContractPatterns.DecimalAmount"/>
    /// before this runs, so an unparsable value cannot reach it.
    /// </summary>
    /// <param name="request">Validated monetary value.</param>
    public static decimal AmountOf(MoneyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return decimal.Parse(request.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the response form of an amount.</summary>
    /// <param name="amount">Amount.</param>
    /// <param name="currency">ISO 4217 currency code.</param>
    public static MoneyResponse ToResponse(decimal amount, string currency) =>
        new(amount.ToString(AmountFormat, CultureInfo.InvariantCulture), currency);
}
