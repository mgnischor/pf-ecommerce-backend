namespace Portfolio.Ordering.Application;

/// <summary>Request to cancel an order (BR-ORD-004).</summary>
/// <param name="CustomerId">The authenticated account; only the order's owner can cancel it.</param>
/// <param name="OrderId">Order identifier.</param>
/// <param name="IdempotencyKey">Client key: replaying it returns the cancelled order and cancels nothing twice.</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
/// <param name="ReasonCode">Machine-readable reason, such as <c>changedMind</c>.</param>
/// <param name="Note">Optional free text, plain text.</param>
internal sealed record CancelOrderCommand(
    Guid CustomerId,
    Guid OrderId,
    string IdempotencyKey,
    int? ExpectedVersion,
    string? ReasonCode,
    string? Note
);
