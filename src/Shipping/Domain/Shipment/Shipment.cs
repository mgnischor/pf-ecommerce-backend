using Portfolio.SharedKernel.Domain;

namespace Portfolio.Shipping.Domain;

/// <summary>
/// The fulfillment of a paid order (aggregate root). Enforces on every change: BR-SHP-001 (what a shipment belongs to),
/// BR-SHP-002 (lifecycle Preparing → InTransit → Delivered | Failed, and Preparing → Cancelled), BR-SHP-003 (what the
/// carrier must tell us when it takes the shipment). References its order by identifier only; the owner of the order is
/// copied in, so reading shipments never needs the Ordering context.
/// </summary>
internal sealed class Shipment : AggregateRoot
{
    /// <summary>Shortest carrier name (BR-SHP-003).</summary>
    public const int MinCarrierLength = 2;

    /// <summary>Longest carrier name (BR-SHP-003).</summary>
    public const int MaxCarrierLength = 60;

    /// <summary>Longest tracking code (BR-SHP-003).</summary>
    public const int MaxTrackingCodeLength = 64;

    /// <summary>Longest failure reason code (BR-SHP-002).</summary>
    public const int MaxReasonLength = 32;

    /// <summary>The order being fulfilled.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The customer that owns the order.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Lifecycle status.</summary>
    public ShipmentStatus Status { get; private set; }

    /// <summary>Carrier name; set when the carrier takes the shipment.</summary>
    public string? Carrier { get; private set; }

    /// <summary>Carrier tracking code, when the carrier gave one.</summary>
    public string? TrackingCode { get; private set; }

    /// <summary>Estimated delivery as a calendar date, when the carrier gave one.</summary>
    public DateOnly? EstimatedDeliveryDate { get; private set; }

    /// <summary>UTC instant the carrier took the shipment.</summary>
    public DateTimeOffset? DispatchedAt { get; private set; }

    /// <summary>UTC instant of the delivery.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>UTC instant the delivery failed.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>Machine-readable reason the delivery failed.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>UTC instant the shipment was cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    private Shipment() { }

    private Shipment(Guid id, Guid orderId, Guid customerId, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Status = ShipmentStatus.Preparing;
    }

    /// <summary>Starts preparing the shipment of a paid order (BR-SHP-001). One shipment per order is enforced by the Application layer and a unique index.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The order's owner.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static Result<Shipment> Prepare(Guid orderId, Guid customerId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return orderId == Guid.Empty || customerId == Guid.Empty
            ? Result<Shipment>.Failure(ShipmentErrors.OrderRequired)
            : Result<Shipment>.Success(new Shipment(NewId(timeProvider), orderId, customerId, timeProvider));
    }

    /// <summary>
    /// Records that the carrier took the shipment (BR-SHP-002: Preparing → InTransit). The carrier is required; the
    /// tracking code and the estimated delivery date are optional (BR-SHP-003). Raises <see cref="ShipmentDispatched"/>.
    /// </summary>
    /// <param name="carrier">Carrier name, 2–60 characters.</param>
    /// <param name="trackingCode">Tracking code, up to 64 ASCII letters, digits, '-' or '_', or <c>null</c>.</param>
    /// <param name="estimatedDeliveryDate">Estimated delivery, not before the day of the dispatch, or <c>null</c>.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Dispatch(
        string? carrier,
        string? trackingCode,
        DateOnly? estimatedDeliveryDate,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status != ShipmentStatus.Preparing)
        {
            return Result.Failure(ShipmentErrors.InvalidStatusTransition(Status, ShipmentStatus.InTransit));
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var error = ValidateDispatch(carrier, trackingCode, estimatedDeliveryDate, today, out var name, out var code);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Status = ShipmentStatus.InTransit;
        Carrier = name;
        TrackingCode = code;
        EstimatedDeliveryDate = estimatedDeliveryDate;
        MarkUpdated(timeProvider);
        DispatchedAt = UpdatedAt;
        AddDomainEvent(ShipmentDispatched.For(this));
        return Result.Success();
    }

    /// <summary>
    /// Whether the carrier already took the shipment with exactly this carrier, tracking code and estimate, so a repeat of
    /// the same hand-over is recognized and a different one is not (BR-SHP-002). Moving on from <c>InTransit</c> (to
    /// delivered or failed) keeps the recorded hand-over, so the repeat is still recognized.
    /// </summary>
    /// <param name="carrier">Carrier of the request.</param>
    /// <param name="trackingCode">Tracking code of the request.</param>
    /// <param name="estimatedDeliveryDate">Estimated delivery of the request.</param>
    public bool WasDispatchedWith(string? carrier, string? trackingCode, DateOnly? estimatedDeliveryDate)
    {
        var code = string.IsNullOrWhiteSpace(trackingCode) ? null : trackingCode.Trim();

        return DispatchedAt is not null
            && string.Equals(Carrier, carrier?.Trim(), StringComparison.Ordinal)
            && string.Equals(TrackingCode, code, StringComparison.Ordinal)
            && EstimatedDeliveryDate == estimatedDeliveryDate;
    }

    /// <summary>
    /// Whether the shipment already ended the way a request says (delivered, or failed for the same reason), so a repeat
    /// of the same outcome is recognized and a different one is not (BR-SHP-002).
    /// </summary>
    /// <param name="delivered"><c>true</c> for a delivery, <c>false</c> for a failure.</param>
    /// <param name="failureReasonCode">The failure reason of the request, when it is a failure.</param>
    public bool HasConcludedWith(bool delivered, string? failureReasonCode) =>
        delivered
            ? Status == ShipmentStatus.Delivered
            : Status == ShipmentStatus.Failed
                && string.Equals(FailureReason, failureReasonCode?.Trim(), StringComparison.Ordinal);

    /// <summary>Records the delivery (BR-SHP-002: InTransit → Delivered). Raises <see cref="ShipmentDelivered"/>.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkDelivered(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var moved = MoveTo(ShipmentStatus.Delivered, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        DeliveredAt = UpdatedAt;
        AddDomainEvent(ShipmentDelivered.For(this));
        return Result.Success();
    }

    /// <summary>Records that the delivery failed (BR-SHP-002: InTransit → Failed). Raises <see cref="ShipmentFailed"/>.</summary>
    /// <param name="reasonCode">Machine-readable reason (2–32 letters, digits or underscores, starting with a letter).</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkFailed(string? reasonCode, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status != ShipmentStatus.InTransit)
        {
            return Result.Failure(ShipmentErrors.InvalidStatusTransition(Status, ShipmentStatus.Failed));
        }

        var reason = reasonCode?.Trim();
        if (!IsValidReason(reason))
        {
            return Result.Failure(ShipmentErrors.FailureReasonInvalid);
        }

        var moved = MoveTo(ShipmentStatus.Failed, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        FailedAt = UpdatedAt;
        FailureReason = reason;
        AddDomainEvent(ShipmentFailed.For(this));
        return Result.Success();
    }

    /// <summary>
    /// Cancels the shipment because its order was cancelled (BR-SHP-002: Preparing → Cancelled). Once the carrier has it,
    /// it can no longer be cancelled here. Raises <see cref="ShipmentCancelled"/>.
    /// </summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Cancel(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var moved = MoveTo(ShipmentStatus.Cancelled, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        CancelledAt = UpdatedAt;
        AddDomainEvent(ShipmentCancelled.For(this));
        return Result.Success();
    }

    private Result MoveTo(ShipmentStatus target, TimeProvider timeProvider)
    {
        if (!IsValidTransition(Status, target))
        {
            return Result.Failure(ShipmentErrors.InvalidStatusTransition(Status, target));
        }

        Status = target;
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    private static bool IsValidTransition(ShipmentStatus from, ShipmentStatus to) =>
        (from, to) switch
        {
            (ShipmentStatus.Preparing, ShipmentStatus.InTransit) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Delivered) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Failed) => true,
            (ShipmentStatus.Preparing, ShipmentStatus.Cancelled) => true,
            _ => false,
        };

    private static Error? ValidateDispatch(
        string? carrier,
        string? trackingCode,
        DateOnly? estimatedDeliveryDate,
        DateOnly today,
        out string? name,
        out string? code
    )
    {
        name = carrier?.Trim();
        code = string.IsNullOrWhiteSpace(trackingCode) ? null : trackingCode.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return ShipmentErrors.CarrierRequired;
        }

        if (name.Length is < MinCarrierLength or > MaxCarrierLength)
        {
            return ShipmentErrors.CarrierLength;
        }

        if (code is not null && !IsValidTrackingCode(code))
        {
            return ShipmentErrors.TrackingCodeInvalid;
        }

        return estimatedDeliveryDate is { } date && date < today ? ShipmentErrors.EstimatedDeliveryInPast : null;
    }

    private static bool IsValidTrackingCode(string code) =>
        code.Length <= MaxTrackingCodeLength && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsValidReason(string? reason) =>
        reason is { Length: >= 2 and <= MaxReasonLength }
        && char.IsAsciiLetter(reason[0])
        && reason.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
