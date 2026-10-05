using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// Stable errors of the Cart rules (docs/business-rules/cart.md). Each error carries its code, field, business rule ID,
/// and message parameters; text is localized at the API boundary.
/// </summary>
internal static class CartErrors
{
    /// <summary>BR-CRT-002: the quantity of one request is outside the allowed range.</summary>
    public static Error QuantityInvalid =>
        Error.Validation(
            "CART_ITEM_QUANTITY_INVALID",
            "quantity",
            "BR-CRT-002",
            ErrorParameters.Of(("min", 1), ("max", ShoppingCart.MaxLineQuantity))
        );

    /// <summary>BR-CRT-002: adding the quantity would take the line above the maximum.</summary>
    public static Error LineQuantityLimit =>
        Error.Validation(
            "CART_LINE_QUANTITY_LIMIT",
            "quantity",
            "BR-CRT-002",
            ErrorParameters.Of(("max", ShoppingCart.MaxLineQuantity))
        );

    /// <summary>BR-CRT-003: the cart already holds the maximum number of distinct products.</summary>
    public static Error ItemLimit =>
        Error.Validation(
            "CART_ITEM_LIMIT",
            "productId",
            "BR-CRT-003",
            ErrorParameters.Of(("max", ShoppingCart.MaxDistinctItems))
        );

    /// <summary>BR-CRT-004: the cart is no longer active, so it cannot change.</summary>
    /// <param name="status">Current status.</param>
    public static Error NotActive(CartStatus status) =>
        Error.Conflict("CART_NOT_ACTIVE", "BR-CRT-004", ErrorParameters.Of(("status", status.ToString())));

    /// <summary>BR-CRT-004: the requested lifecycle transition is not allowed.</summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Requested status.</param>
    public static Error InvalidStatusTransition(CartStatus from, CartStatus to) =>
        Error.Conflict(
            "CART_INVALID_STATUS_TRANSITION",
            "BR-CRT-004",
            ErrorParameters.Of(("from", from.ToString()), ("to", to.ToString()))
        );

    /// <summary>BR-CRT-004: an empty cart cannot be checked out.</summary>
    public static Error EmptyCart => Error.Conflict("CART_EMPTY", "BR-CRT-004");

    /// <summary>BR-CRT-005: the product is not known to the cart, or it is not sellable.</summary>
    public static Error ProductNotAvailable =>
        Error.Validation("CART_PRODUCT_NOT_AVAILABLE", "productId", "BR-CRT-005");

    /// <summary>BR-CRT-005: the product is priced in another currency than the cart.</summary>
    /// <param name="cartCurrency">Currency of the cart.</param>
    public static Error CurrencyMismatch(string cartCurrency) =>
        Error.Validation(
            "CART_CURRENCY_MISMATCH",
            "productId",
            "BR-CRT-005",
            ErrorParameters.Of(("currency", cartCurrency))
        );

    /// <summary>BR-CRT-001: the customer already has an active cart in another currency.</summary>
    /// <param name="currency">Currency of the cart that is already open.</param>
    public static Error AlreadyOpen(string currency) =>
        Error.Conflict("CART_ALREADY_OPEN", "BR-CRT-001", ErrorParameters.Of(("currency", currency)));

    /// <summary>The cart does not exist, or belongs to another customer.</summary>
    public static Error NotFound => Error.NotFound("CART_NOT_FOUND");

    /// <summary>The line does not exist in the cart.</summary>
    public static Error ItemNotFound => Error.NotFound("CART_ITEM_NOT_FOUND");
}
