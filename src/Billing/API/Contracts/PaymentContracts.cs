using System.ComponentModel.DataAnnotations;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Billing.API.Contracts;

/// <summary>Status of a payment. An open set: clients must tolerate new values.</summary>
public enum PaymentStatusContract
{
    /// <summary>Waiting for the provider.</summary>
    Pending,

    /// <summary>Authorized but not captured.</summary>
    Authorized,

    /// <summary>Captured.</summary>
    Captured,

    /// <summary>Refused or failed.</summary>
    Failed,

    /// <summary>Fully refunded.</summary>
    Refunded,
}

/// <summary>Status of a refund. An open set: clients must tolerate new values.</summary>
public enum RefundStatusContract
{
    /// <summary>Waiting for approval or settlement.</summary>
    Pending,

    /// <summary>Settled by the provider.</summary>
    Succeeded,

    /// <summary>Rejected by the provider.</summary>
    Failed,
}

/// <summary>Request to refund part or all of a captured payment.</summary>
/// <param name="Amount">Amount to refund; cannot exceed the refundable balance.</param>
/// <param name="ReasonCode">Machine-readable reason, such as <c>damagedItem</c>.</param>
public sealed record RefundPaymentRequest(
    [Required] MoneyRequest Amount,
    [Required, StringLength(32, MinimumLength = 2)] string ReasonCode
);

/// <summary>Payment resource. Only masked payment-method data is exposed.</summary>
/// <param name="Id">Payment identifier.</param>
/// <param name="OrderId">Order being paid.</param>
/// <param name="Status">Payment status.</param>
/// <param name="Amount">Amount of the payment.</param>
/// <param name="MethodBrand">Card brand, omitted for other methods.</param>
/// <param name="MethodLastFour">Last four digits of the card, omitted for other methods.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    PaymentStatusContract Status,
    MoneyResponse Amount,
    string? MethodBrand,
    string? MethodLastFour,
    int Version
);

/// <summary>Refund resource.</summary>
/// <param name="Id">Refund identifier.</param>
/// <param name="PaymentId">Refunded payment.</param>
/// <param name="Status">Refund status.</param>
/// <param name="Amount">Refunded amount.</param>
public sealed record RefundResponse(Guid Id, Guid PaymentId, RefundStatusContract Status, MoneyResponse Amount);
