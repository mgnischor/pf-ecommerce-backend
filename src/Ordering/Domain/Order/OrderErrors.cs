using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// Stable errors of the Ordering rules (docs/business-rules/ordering.md). Each error carries its code, field, business
/// rule ID, and message parameters; text is localized at the API boundary.
/// </summary>
internal static class OrderErrors
{
    /// <summary>BR-ORD-001: an order needs at least one line.</summary>
    public static Error NoItems => Error.Validation("ORDER_NO_ITEMS", "items", "BR-ORD-001");

    /// <summary>BR-ORD-001: the order has more lines than allowed.</summary>
    public static Error TooManyItems =>
        Error.Validation("ORDER_TOO_MANY_ITEMS", "items", "BR-ORD-001", ErrorParameters.Of(("max", Order.MaxItems)));

    /// <summary>BR-ORD-001: a line is missing data, or its quantity or unit price is outside the allowed range.</summary>
    public static Error ItemInvalid => Error.Validation("ORDER_ITEM_INVALID", "items", "BR-ORD-001");

    /// <summary>BR-ORD-001: a line's quantity is outside the allowed range.</summary>
    public static Error ItemQuantityInvalid =>
        Error.Validation(
            "ORDER_ITEM_QUANTITY_INVALID",
            "items",
            "BR-ORD-001",
            ErrorParameters.Of(("min", 1), ("max", Order.MaxLineQuantity))
        );

    /// <summary>BR-ORD-001: the lines are not all priced in the same currency.</summary>
    public static Error CurrencyMixed => Error.Validation("ORDER_CURRENCY_MIXED", "items", "BR-ORD-001");

    /// <summary>BR-ORD-001: the order is placed for nobody, or its number is missing.</summary>
    public static Error CustomerRequired => Error.Validation("ORDER_CUSTOMER_REQUIRED", "customerId", "BR-ORD-001");

    /// <summary>BR-ORD-005: the order number is missing.</summary>
    public static Error NumberRequired => Error.Validation("ORDER_NUMBER_REQUIRED", "number", "BR-ORD-005");

    /// <summary>BR-ORD-003: the requested lifecycle transition is not allowed from the current status.</summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Requested status.</param>
    public static Error InvalidStatusTransition(OrderStatus from, OrderStatus to) =>
        Error.Conflict(
            "ORDER_INVALID_STATUS_TRANSITION",
            "BR-ORD-003",
            ErrorParameters.Of(("from", from.ToString()), ("to", to.ToString()))
        );

    /// <summary>BR-ORD-004: the cancellation reason is missing.</summary>
    public static Error ReasonRequired => Error.Validation("ORDER_REASON_REQUIRED", "reasonCode", "BR-ORD-004");

    /// <summary>BR-ORD-004: the cancellation reason is not 2 to 32 letters, digits or underscores starting with a letter.</summary>
    public static Error ReasonInvalid =>
        Error.Validation(
            "ORDER_REASON_INVALID",
            "reasonCode",
            "BR-ORD-004",
            ErrorParameters.Of(("min", Order.MinReasonLength), ("max", Order.MaxReasonLength))
        );

    /// <summary>BR-ORD-004: the cancellation note is too long.</summary>
    public static Error NoteTooLong =>
        Error.Validation("ORDER_NOTE_TOO_LONG", "note", "BR-ORD-004", ErrorParameters.Of(("max", Order.MaxNoteLength)));

    /// <summary>BR-ORD-004: the idempotency key was already used for a different cancellation.</summary>
    public static Error IdempotencyKeyReused => Error.Validation("IDEMPOTENCY_KEY_REUSED", ruleId: "BR-ORD-004");

    /// <summary>The <c>If-Match</c> version is stale or malformed: the order changed since the client read it.</summary>
    public static Error VersionMismatch => Error.PreconditionFailed("ORDER_VERSION_MISMATCH");

    /// <summary>BR-ORD-006: the sort field is not one of the allowed ones.</summary>
    public static Error SortFieldNotAllowed =>
        Error.BadRequest("SORT_FIELD_NOT_ALLOWED", "sort", ErrorParameters.Of(("allowed", "placedAt, total")));

    /// <summary>BR-ORD-006: the cursor is malformed, forged, or was issued for another query.</summary>
    public static Error CursorInvalid => Error.BadRequest("PAGE_CURSOR_INVALID", "cursor");

    /// <summary>The order does not exist, or belongs to another customer.</summary>
    public static Error NotFound => Error.NotFound("ORDER_NOT_FOUND");
}
