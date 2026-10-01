using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Inventory.API.Contracts;

/// <summary>Request to start tracking the stock of a SKU. The item starts with no units on hand.</summary>
/// <param name="Sku">Stock-keeping unit, 4–32 ASCII letters, digits, hyphen or underscore (case-insensitive).</param>
internal sealed record OpenInventoryItemRequest(
    [
        Required,
        RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)
    ]
        string Sku
);

/// <summary>Manual correction of the stock level, recorded as an append-only movement.</summary>
/// <param name="Delta">Units to add (positive) or remove (negative); never zero.</param>
/// <param name="ReasonCode">Machine-readable reason, such as <c>stocktake</c> or <c>damage</c>.</param>
internal sealed record AdjustStockRequest(
    [Range(-100000, 100000)] [property: JsonRequired] int Delta,
    [Required, StringLength(32, MinimumLength = 2)] string ReasonCode
);

/// <summary>Stock level of a SKU.</summary>
/// <param name="Sku">Stock-keeping unit.</param>
/// <param name="OnHand">Physical units in stock.</param>
/// <param name="Reserved">Units held by open reservations.</param>
/// <param name="Available">Units that can still be sold (<c>onHand - reserved</c>).</param>
/// <param name="Version">Resource version; also returned as the <c>ETag</c> header.</param>
internal sealed record InventoryItemResponse(string Sku, int OnHand, int Reserved, int Available, int Version);
