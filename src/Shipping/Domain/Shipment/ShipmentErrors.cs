using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>
/// Stable errors of the Shipping rules (docs/business-rules/shipping.md). Each error carries its code, field, business
/// rule ID, and message parameters; text is localized at the API boundary.
/// </summary>
internal static class ShipmentErrors
{
    /// <summary>BR-SHP-001: a shipment needs an order and a customer.</summary>
    public static Error OrderRequired => Error.Validation("SHIPMENT_ORDER_REQUIRED", "orderId", "BR-SHP-001");

    /// <summary>BR-SHP-003: the carrier is missing.</summary>
    public static Error CarrierRequired => Error.Validation("SHIPMENT_CARRIER_REQUIRED", "carrier", "BR-SHP-003");

    /// <summary>BR-SHP-003: the carrier name is outside the allowed length.</summary>
    public static Error CarrierLength =>
        Error.Validation(
            "SHIPMENT_CARRIER_LENGTH",
            "carrier",
            "BR-SHP-003",
            ErrorParameters.Of(("min", Shipment.MinCarrierLength), ("max", Shipment.MaxCarrierLength))
        );

    /// <summary>BR-SHP-003: the tracking code has characters other than ASCII letters, digits, '-' or '_', or is too long.</summary>
    public static Error TrackingCodeInvalid =>
        Error.Validation(
            "SHIPMENT_TRACKING_CODE_INVALID",
            "trackingCode",
            "BR-SHP-003",
            ErrorParameters.Of(("max", Shipment.MaxTrackingCodeLength))
        );

    /// <summary>BR-SHP-003: the estimated delivery date is before the day the shipment was handed to the carrier.</summary>
    public static Error EstimatedDeliveryInPast =>
        Error.Validation("SHIPMENT_ESTIMATED_DELIVERY_IN_PAST", "estimatedDeliveryDate", "BR-SHP-003");

    /// <summary>BR-SHP-002: the failure reason is missing or not 2 to 32 letters, digits or underscores starting with a letter.</summary>
    public static Error FailureReasonInvalid =>
        Error.Validation(
            "SHIPMENT_FAILURE_REASON_INVALID",
            "reasonCode",
            "BR-SHP-002",
            ErrorParameters.Of(("min", 2), ("max", Shipment.MaxReasonLength))
        );

    /// <summary>BR-SHP-002: the requested lifecycle transition is not allowed from the current status.</summary>
    /// <param name="from">Current status.</param>
    /// <param name="to">Requested status.</param>
    public static Error InvalidStatusTransition(ShipmentStatus from, ShipmentStatus to) =>
        Error.Conflict(
            "SHIPMENT_INVALID_STATUS_TRANSITION",
            "BR-SHP-002",
            ErrorParameters.Of(("from", from.ToString()), ("to", to.ToString()))
        );

    /// <summary>BR-SHP-006: the sync event lacks data its kind requires, so no retry can make it valid.</summary>
    public static Error MalformedOrderEvent => Error.Validation("SHIPPING_ORDER_EVENT_MALFORMED");

    /// <summary>The shipment does not exist.</summary>
    public static Error NotFound => Error.NotFound("SHIPMENT_NOT_FOUND");

    /// <summary>BR-SHP-004: the order does not exist for this customer (unknown, or someone else's).</summary>
    public static Error OrderNotFound => Error.NotFound("ORDER_NOT_FOUND");

    /// <summary>BR-SHP-004: the cursor is malformed, forged, or was issued for another query.</summary>
    public static Error CursorInvalid => Error.BadRequest("PAGE_CURSOR_INVALID", "cursor");
}
