using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Checkout.API.Contracts;

/// <summary>Status of a checkout. An open set: clients must tolerate new values.</summary>
public enum CheckoutStatusContract
{
    /// <summary>Accepted and being processed (stock reservation, payment authorization).</summary>
    Processing,

    /// <summary>Completed; an order exists.</summary>
    Completed,

    /// <summary>Failed; nothing was charged.</summary>
    Failed,
}

/// <summary>Request to convert a cart into a purchase. Totals are computed by the server.</summary>
/// <param name="ShippingAddressId">Saved address of the authenticated customer.</param>
/// <param name="ShippingMethodCode">Shipping option offered for the cart.</param>
/// <param name="PaymentMethodToken">Token issued by the payment provider. Card numbers are never accepted.</param>
public sealed record StartCheckoutRequest(
    [Required] [property: JsonRequired] Guid ShippingAddressId,
    [Required, StringLength(32, MinimumLength = 2)] string ShippingMethodCode,
    [Required, StringLength(256, MinimumLength = 8)] string PaymentMethodToken
);

/// <summary>Checkout resource.</summary>
/// <param name="Id">Checkout identifier.</param>
/// <param name="CartId">Cart being converted.</param>
/// <param name="Status">Checkout status.</param>
/// <param name="Total">Amount to pay, computed by the server.</param>
/// <param name="OrderId">Order created by a completed checkout, omitted otherwise.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
public sealed record CheckoutResponse(
    Guid Id,
    Guid CartId,
    CheckoutStatusContract Status,
    MoneyResponse Total,
    Guid? OrderId,
    int Version
);
