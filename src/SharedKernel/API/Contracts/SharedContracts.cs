using System.ComponentModel.DataAnnotations;

namespace Portfolio.SharedKernel.API.Contracts;

/// <summary>Regular expressions shared by request validation; timeouts guard against ReDoS.</summary>
public static class ContractPatterns
{
    /// <summary>Decimal string with at most 4 fraction digits (<c>"25.90"</c>); never a JSON number.</summary>
    public const string DecimalAmount = @"^\d{1,15}(\.\d{1,4})?$";

    /// <summary>ISO 4217 currency code.</summary>
    public const string CurrencyCode = "^[A-Za-z]{3}$";

    /// <summary>SKU: 4–32 ASCII letters, digits, hyphen, underscore (BR-CAT-004).</summary>
    public const string Sku = "^[A-Za-z0-9_-]{4,32}$";

    /// <summary>Allowlisted sort expression such as <c>name</c> or <c>-createdAt</c>.</summary>
    public const string SortExpression = "^-?[a-zA-Z]{2,32}$";

    /// <summary>Regex timeout in milliseconds for <see cref="RegularExpressionAttribute"/>.</summary>
    public const int TimeoutMilliseconds = 100;
}

/// <summary>Monetary value sent by a client.</summary>
/// <param name="Amount">Decimal string with at most 4 fraction digits, for example <c>"25.90"</c>.</param>
/// <param name="Currency">ISO 4217 currency code, for example <c>BRL</c>.</param>
public sealed record MoneyRequest(
    [
        Required,
        RegularExpression(
            ContractPatterns.DecimalAmount,
            MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
        )
    ]
        string Amount,
    [
        Required,
        RegularExpression(
            ContractPatterns.CurrencyCode,
            MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
        )
    ]
        string Currency
);

/// <summary>Monetary value returned by the API.</summary>
/// <param name="Amount">Decimal string, for example <c>"25.90"</c>.</param>
/// <param name="Currency">ISO 4217 currency code.</param>
public sealed record MoneyResponse(string Amount, string Currency);

/// <summary>One page of a cursor-paginated collection (ai/API_CONTRACTS.md §6).</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items of the page; an empty page is <c>[]</c>, never <c>null</c>.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <c>null</c> on the last page.</param>
/// <param name="HasMore">Whether another page exists.</param>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
