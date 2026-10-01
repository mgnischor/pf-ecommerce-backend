using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// One line of the stock ledger (BR-INV-003): a signed change of the physical stock, who made it, why, and the
/// level it left behind. Append-only: there is no operation that edits or deletes a movement.
/// </summary>
internal sealed class StockMovement : Entity
{
    /// <summary>Minimum length of a reason code (BR-INV-002).</summary>
    public const int MinReasonLength = 2;

    /// <summary>Maximum length of a reason code (BR-INV-002).</summary>
    public const int MaxReasonLength = 32;

    /// <summary>The inventory item the movement belongs to.</summary>
    public Guid InventoryItemId { get; private set; }

    /// <summary>Units added (positive) or removed (negative).</summary>
    public int Delta { get; private set; }

    /// <summary>Normalized machine-readable reason, such as <c>stocktake</c> or <c>damage</c>.</summary>
    public string ReasonCode { get; private set; }

    /// <summary>Physical stock right after the movement.</summary>
    public int OnHandAfter { get; private set; }

    /// <summary>Account that recorded the movement.</summary>
    public Guid RecordedBy { get; private set; }

    /// <summary>Client-supplied key that makes a retry of the same adjustment a replay; <c>null</c> for system movements.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private StockMovement() { }
#pragma warning restore CS8618

    private StockMovement(
        Guid id,
        Guid inventoryItemId,
        int delta,
        string reasonCode,
        int onHandAfter,
        Guid recordedBy,
        string? idempotencyKey,
        TimeProvider timeProvider
    )
        : base(id, timeProvider)
    {
        InventoryItemId = inventoryItemId;
        Delta = delta;
        ReasonCode = reasonCode;
        OnHandAfter = onHandAfter;
        RecordedBy = recordedBy;
        IdempotencyKey = idempotencyKey;
    }

    public static StockMovement Record(
        Guid inventoryItemId,
        int delta,
        string reasonCode,
        int onHandAfter,
        Guid recordedBy,
        string? idempotencyKey,
        TimeProvider timeProvider
    ) =>
        new(
            NewId(timeProvider),
            inventoryItemId,
            delta,
            reasonCode,
            onHandAfter,
            recordedBy,
            idempotencyKey,
            timeProvider
        );

    /// <summary>
    /// Normalizes a raw reason code (trimmed, lowercase) and validates it (BR-INV-002): 2 to 32 characters,
    /// ASCII lowercase letters, digits or underscores, starting with a letter.
    /// </summary>
    /// <param name="reasonCode">Raw reason code.</param>
    /// <returns>The normalized reason code, or the violated rule.</returns>
    public static Result<string> NormalizeReason(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return Result<string>.Failure(InventoryErrors.ReasonRequired);
        }

        var normalized = reasonCode.Trim().ToLowerInvariant();

        var valid =
            normalized.Length is >= MinReasonLength and <= MaxReasonLength
            && char.IsAsciiLetterLower(normalized[0])
            && normalized.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

        return valid ? Result<string>.Success(normalized) : Result<string>.Failure(InventoryErrors.ReasonInvalid);
    }

    /// <summary>Whether this movement is the one that an adjustment of <paramref name="delta"/> for <paramref name="reasonCode"/> would record.</summary>
    /// <param name="delta">Requested signed quantity.</param>
    /// <param name="reasonCode">Requested reason code; an invalid code never matches.</param>
    public bool IsEquivalentTo(int delta, string? reasonCode) =>
        Delta == delta
        && NormalizeReason(reasonCode) is { IsSuccess: true } reason
        && string.Equals(reason.Value, ReasonCode, StringComparison.Ordinal);
}
