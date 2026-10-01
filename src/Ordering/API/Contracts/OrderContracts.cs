using System.ComponentModel.DataAnnotations;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Ordering.API.Contracts;

/// <summary>Status of an order. An open set: clients must tolerate new values.</summary>
internal enum OrderStatusContract
{
    /// <summary>Placed and waiting for payment.</summary>
    AwaitingPayment,

    /// <summary>Paid; fulfillment can start.</summary>
    Paid,

    /// <summary>Handed to the carrier.</summary>
    Shipped,

    /// <summary>Delivered to the customer.</summary>
    Delivered,

    /// <summary>Cancelled before fulfillment completed.</summary>
    Cancelled,
}

/// <summary>Request to cancel an order.</summary>
/// <param name="ReasonCode">Machine-readable reason, such as <c>changedMind</c>.</param>
/// <param name="Note">Optional free text, up to 500 characters, stored as plain text.</param>
internal sealed record CancelOrderRequest(
    [Required, StringLength(32, MinimumLength = 2)] string ReasonCode,
    [StringLength(500)] string? Note = null
);

/// <summary>Order as seen in a collection.</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Number">Human-readable order number.</param>
/// <param name="Status">Order status.</param>
/// <param name="Total">Order total captured at purchase time.</param>
/// <param name="PlacedAt">UTC instant the order was placed.</param>
internal sealed record OrderSummaryResponse(
    Guid Id,
    string Number,
    OrderStatusContract Status,
    MoneyResponse Total,
    DateTimeOffset PlacedAt
);

/// <summary>One line of an order, priced as at purchase time.</summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Sku">Stock-keeping unit at purchase time.</param>
/// <param name="Name">Product name at purchase time.</param>
/// <param name="Quantity">Units purchased.</param>
/// <param name="UnitPrice">Unit price at purchase time.</param>
/// <param name="LineTotal">Unit price multiplied by quantity.</param>
internal sealed record OrderItemResponse(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    MoneyResponse UnitPrice,
    MoneyResponse LineTotal
);

/// <summary>Full order resource.</summary>
/// <param name="Id">Order identifier.</param>
/// <param name="Number">Human-readable order number.</param>
/// <param name="Status">Order status.</param>
/// <param name="Items">Lines of the order.</param>
/// <param name="Total">Order total captured at purchase time.</param>
/// <param name="PlacedAt">UTC instant the order was placed.</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
/// <param name="AllowedActions">Actions the state machine currently allows, such as <c>cancel</c>.</param>
internal sealed record OrderResponse(
    Guid Id,
    string Number,
    OrderStatusContract Status,
    IReadOnlyList<OrderItemResponse> Items,
    MoneyResponse Total,
    DateTimeOffset PlacedAt,
    int Version,
    IReadOnlyList<string> AllowedActions
);
