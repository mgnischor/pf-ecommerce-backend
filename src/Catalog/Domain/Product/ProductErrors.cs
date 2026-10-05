using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Stable errors of the Catalog product rules (docs/business-rules/catalog.md).
/// Each error carries its code, field, business rule ID, and message parameters; text is localized at the API boundary.
/// </summary>
internal static class ProductErrors
{
    /// <summary>BR-CAT-001: the product name is missing.</summary>
    public static Error NameRequired => Error.Validation("PRODUCT_NAME_REQUIRED", "name", "BR-CAT-001");

    /// <summary>BR-CAT-001: the product name is outside the allowed length.</summary>
    public static Error NameLength =>
        Error.Validation(
            "PRODUCT_NAME_LENGTH",
            "name",
            "BR-CAT-001",
            ErrorParameters.Of(("min", Product.MinNameLength), ("max", Product.MaxNameLength))
        );

    /// <summary>BR-CAT-001: the product description is too long.</summary>
    public static Error DescriptionTooLong =>
        Error.Validation(
            "PRODUCT_DESCRIPTION_TOO_LONG",
            "description",
            "BR-CAT-001",
            ErrorParameters.Of(("max", Product.MaxDescriptionLength))
        );

    /// <summary>BR-CAT-002: the product price is not greater than zero.</summary>
    public static Error PriceMustBePositive =>
        Error.Validation("PRODUCT_PRICE_MUST_BE_POSITIVE", "price", "BR-CAT-002");

    /// <summary>BR-CAT-003: the requested lifecycle transition is not allowed.</summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Requested status.</param>
    public static Error InvalidStatusTransition(ProductStatus from, ProductStatus to) =>
        Error.Conflict(
            "PRODUCT_INVALID_STATUS_TRANSITION",
            "BR-CAT-003",
            ErrorParameters.Of(("from", from.ToString()), ("to", to.ToString()))
        );

    /// <summary>BR-CAT-004: the SKU is missing.</summary>
    public static Error SkuRequired => Error.Validation("SKU_REQUIRED", "sku", "BR-CAT-004");

    /// <summary>BR-CAT-004: the SKU is outside the allowed length.</summary>
    public static Error SkuLength =>
        Error.Validation(
            "SKU_LENGTH",
            "sku",
            "BR-CAT-004",
            ErrorParameters.Of(("min", Sku.MinLength), ("max", Sku.MaxLength))
        );

    /// <summary>BR-CAT-004: the SKU contains characters other than ASCII letters, digits, '-' or '_'.</summary>
    public static Error SkuInvalidCharacters => Error.Validation("SKU_INVALID_CHARACTERS", "sku", "BR-CAT-004");

    /// <summary>BR-CAT-005: another active product already uses the SKU.</summary>
    public static Error SkuAlreadyExists => Error.Conflict("PRODUCT_SKU_ALREADY_EXISTS", "BR-CAT-005");

    /// <summary>The product does not exist (or was deleted).</summary>
    public static Error NotFound => Error.NotFound("PRODUCT_NOT_FOUND");

    /// <summary>The <c>If-Match</c> version is stale or malformed: the product changed since the client read it.</summary>
    public static Error VersionMismatch => Error.PreconditionFailed("PRODUCT_VERSION_MISMATCH");

    /// <summary>BR-CAT-008: the idempotency key was already used for a different request.</summary>
    public static Error IdempotencyKeyReused => Error.Validation("IDEMPOTENCY_KEY_REUSED", ruleId: "BR-CAT-008");

    /// <summary>BR-CAT-006: the requested sort field is not one of the allowed ones.</summary>
    public static Error SortFieldNotAllowed =>
        Error.BadRequest("SORT_FIELD_NOT_ALLOWED", "sort", ErrorParameters.Of(("allowed", "name, price, createdAt")));

    /// <summary>BR-CAT-006: the cursor is malformed, forged, or was issued for another query.</summary>
    public static Error CursorInvalid => Error.BadRequest("PAGE_CURSOR_INVALID", "cursor");
}
