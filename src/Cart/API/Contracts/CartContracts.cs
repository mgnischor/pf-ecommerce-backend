using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Cart.API.Contracts;

/// <summary>Status of a cart. An open set: clients must tolerate new values.</summary>
public enum CartStatusContract
{
    /// <summary>Open for changes.</summary>
    Active,

    /// <summary>Converted into a checkout.</summary>
    CheckedOut,

    /// <summary>Expired after inactivity.</summary>
    Expired,
}

/// <summary>Request to open a cart.</summary>
/// <param name="Currency">ISO 4217 currency the cart is priced in.</param>
public sealed record CreateCartRequest(
    [
        Required,
        RegularExpression(
            ContractPatterns.CurrencyCode,
            MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
        )
    ]
        string Currency
);

/// <summary>Request to add a product to a cart. Prices are always taken from the catalog, never from the client.</summary>
/// <param name="ProductId">Product to add.</param>
/// <param name="Quantity">Units to add, 1–99.</param>
public sealed record AddCartItemRequest(
    [Required] [property: JsonRequired] Guid ProductId,
    [Range(1, 99)] [property: JsonRequired] int Quantity
);

/// <summary>One line of a cart.</summary>
/// <param name="Id">Line identifier.</param>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="Name">Product name at the time of reading.</param>
/// <param name="Quantity">Units in the line.</param>
/// <param name="UnitPrice">Current unit price.</param>
/// <param name="LineTotal">Unit price multiplied by quantity, computed by the server.</param>
public sealed record CartItemResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    MoneyResponse UnitPrice,
    MoneyResponse LineTotal
);

/// <summary>Cart resource.</summary>
/// <param name="Id">Cart identifier.</param>
/// <param name="Status">Cart status.</param>
/// <param name="Items">Lines of the cart; <c>[]</c> when empty.</param>
/// <param name="Subtotal">Sum of the line totals, computed by the server.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
/// <param name="AllowedActions">Actions currently allowed, such as <c>checkout</c>.</param>
public sealed record CartResponse(
    Guid Id,
    CartStatusContract Status,
    IReadOnlyList<CartItemResponse> Items,
    MoneyResponse Subtotal,
    int Version,
    IReadOnlyList<string> AllowedActions
);
