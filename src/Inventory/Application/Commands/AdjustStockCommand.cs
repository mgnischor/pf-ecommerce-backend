namespace Portfolio.Inventory.Application;

/// <summary>Request to record a manual stock movement.</summary>
/// <param name="ActorId">The caller's account, recorded in the ledger.</param>
/// <param name="Sku">Stock-keeping unit of the item.</param>
/// <param name="Delta">Units to add (positive) or remove (negative).</param>
/// <param name="ReasonCode">Machine-readable reason, such as <c>stocktake</c> or <c>damage</c>.</param>
/// <param name="IdempotencyKey">Client key: replaying it never adjusts twice.</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
internal sealed record AdjustStockCommand(
    Guid ActorId,
    string? Sku,
    int Delta,
    string? ReasonCode,
    string IdempotencyKey,
    int? ExpectedVersion
);
