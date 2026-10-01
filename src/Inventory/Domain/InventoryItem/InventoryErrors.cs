using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// Stable errors of the Inventory rules (docs/business-rules/inventory.md).
/// Each error carries its code, field, business rule ID, and message parameters; text is localized at the API boundary.
/// </summary>
internal static class InventoryErrors
{
    /// <summary>BR-INV-007: the SKU is missing.</summary>
    public static Error SkuRequired => Error.Validation("INVENTORY_SKU_REQUIRED", "sku", "BR-INV-007");

    /// <summary>BR-INV-007: the SKU is outside the allowed length.</summary>
    public static Error SkuLength =>
        Error.Validation(
            "INVENTORY_SKU_LENGTH",
            "sku",
            "BR-INV-007",
            ErrorParameters.Of(("min", Sku.MinLength), ("max", Sku.MaxLength))
        );

    /// <summary>BR-INV-007: the SKU contains characters other than ASCII letters, digits, '-' or '_'.</summary>
    public static Error SkuInvalidCharacters =>
        Error.Validation("INVENTORY_SKU_INVALID_CHARACTERS", "sku", "BR-INV-007");

    /// <summary>BR-INV-002: an adjustment of zero units moves nothing.</summary>
    public static Error AdjustmentDeltaZero => Error.Validation("STOCK_ADJUSTMENT_DELTA_ZERO", "delta", "BR-INV-002");

    /// <summary>BR-INV-002: the adjustment is larger than one movement may carry.</summary>
    public static Error AdjustmentDeltaRange =>
        Error.Validation(
            "STOCK_ADJUSTMENT_DELTA_RANGE",
            "delta",
            "BR-INV-002",
            ErrorParameters.Of(("max", InventoryItem.MaxAdjustmentMagnitude))
        );

    /// <summary>BR-INV-002: the reason code is missing.</summary>
    public static Error ReasonRequired => Error.Validation("STOCK_REASON_REQUIRED", "reasonCode", "BR-INV-002");

    /// <summary>BR-INV-002: the reason code is not 2 to 32 lowercase letters, digits or underscores starting with a letter.</summary>
    public static Error ReasonInvalid =>
        Error.Validation(
            "STOCK_REASON_INVALID",
            "reasonCode",
            "BR-INV-002",
            ErrorParameters.Of(("min", StockMovement.MinReasonLength), ("max", StockMovement.MaxReasonLength))
        );

    /// <summary>BR-INV-001: the adjustment would take the physical stock below the reserved quantity.</summary>
    /// <param name="onHand">Physical units before the adjustment.</param>
    /// <param name="reserved">Units held by reservations.</param>
    public static Error BelowReserved(int onHand, int reserved) =>
        Error.Validation(
            "STOCK_BELOW_RESERVED",
            "delta",
            "BR-INV-001",
            ErrorParameters.Of(("onHand", onHand), ("reserved", reserved))
        );

    /// <summary>BR-INV-001: the adjustment would take the physical stock above the supported maximum.</summary>
    public static Error AboveMaximum =>
        Error.Validation(
            "STOCK_ABOVE_MAXIMUM",
            "delta",
            "BR-INV-001",
            ErrorParameters.Of(("max", InventoryItem.MaxOnHand))
        );

    /// <summary>BR-INV-005 and BR-INV-006: a quantity to reserve or release is not positive.</summary>
    public static Error QuantityMustBePositive =>
        Error.Validation("STOCK_QUANTITY_MUST_BE_POSITIVE", "quantity", "BR-INV-005");

    /// <summary>BR-INV-005: more units were requested than are available.</summary>
    /// <param name="requested">Units requested.</param>
    /// <param name="available">Units that can still be sold.</param>
    public static Error InsufficientAvailable(int requested, int available) =>
        Error.Conflict(
            "STOCK_INSUFFICIENT_AVAILABLE",
            "BR-INV-005",
            ErrorParameters.Of(("requested", requested), ("available", available))
        );

    /// <summary>BR-INV-006: more units were released than are reserved.</summary>
    /// <param name="requested">Units to release.</param>
    /// <param name="reserved">Units held by reservations.</param>
    public static Error ReleaseExceedsReserved(int requested, int reserved) =>
        Error.Conflict(
            "STOCK_RELEASE_EXCEEDS_RESERVED",
            "BR-INV-006",
            ErrorParameters.Of(("requested", requested), ("reserved", reserved))
        );

    /// <summary>BR-INV-008: an inventory item already exists for the SKU.</summary>
    public static Error ItemAlreadyExists => Error.Conflict("INVENTORY_ITEM_ALREADY_EXISTS", "BR-INV-008");

    /// <summary>BR-INV-003: the idempotency key was already used for a different adjustment.</summary>
    public static Error IdempotencyKeyReused => Error.Conflict("IDEMPOTENCY_KEY_REUSED", "BR-INV-003");

    /// <summary>The inventory item does not exist (or was deleted).</summary>
    public static Error NotFound => Error.NotFound("INVENTORY_ITEM_NOT_FOUND");

    /// <summary>The <c>If-Match</c> version is stale or malformed.</summary>
    public static Error VersionMismatch => Error.PreconditionFailed("INVENTORY_VERSION_MISMATCH");
}
