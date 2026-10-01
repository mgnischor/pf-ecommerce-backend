using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.Domain;

/// <summary>
/// Stock of one SKU, aggregate root of the Inventory context. Enforces on every state change:
/// BR-INV-001 (stock level bounds), BR-INV-002 (adjustment constraints), BR-INV-003 (append-only ledger),
/// BR-INV-004 (available stock), BR-INV-005 (reservations stay within availability) and
/// BR-INV-006 (releases stay within reservations). SKU format (BR-INV-007) is enforced by <see cref="Sku"/>;
/// one item per SKU (BR-INV-008) needs the repository and is enforced by the Application layer plus a unique index.
/// </summary>
internal sealed class InventoryItem : AggregateRoot
{
    /// <summary>Largest absolute quantity one adjustment may carry (BR-INV-002).</summary>
    public const int MaxAdjustmentMagnitude = 100_000;

    /// <summary>Largest physical stock the item may hold (BR-INV-001); keeps every sum far from overflow.</summary>
    public const int MaxOnHand = 10_000_000;

    private readonly List<StockMovement> _movements = [];

    /// <summary>Stock-keeping unit. Unique among active items.</summary>
    public Sku Sku { get; private set; }

    /// <summary>Physical units in stock.</summary>
    public int OnHand { get; private set; }

    /// <summary>Units held by open reservations.</summary>
    public int Reserved { get; private set; }

    /// <summary>Units that can still be sold (BR-INV-004).</summary>
    public int Available => OnHand - Reserved;

    /// <summary>
    /// Movements recorded through this instance. The full history lives in the ledger table and is queried from
    /// there, never loaded into the aggregate; adding to this list is how a new movement is persisted.
    /// </summary>
    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private InventoryItem() { }
#pragma warning restore CS8618

    private InventoryItem(Guid id, Sku sku, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        Sku = sku;
    }

    /// <summary>Opens an item with no stock; stock arrives through <see cref="Adjust"/>.</summary>
    /// <param name="sku">Stock-keeping unit.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static InventoryItem Open(Sku sku, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var item = new InventoryItem(NewId(timeProvider), sku, timeProvider);
        item.AddDomainEvent(InventoryItemOpened.For(item));

        return item;
    }

    /// <summary>
    /// Records a manual stock movement (BR-INV-001, BR-INV-002, BR-INV-003). Nothing changes when a rule fails.
    /// </summary>
    /// <param name="delta">Units to add (positive) or remove (negative); never zero, at most <see cref="MaxAdjustmentMagnitude"/>.</param>
    /// <param name="reasonCode">Machine-readable reason, such as <c>stocktake</c> or <c>damage</c>.</param>
    /// <param name="recordedBy">Account making the adjustment.</param>
    /// <param name="idempotencyKey">Key of the request, stored with the movement so a retry can be recognised.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns>The ledger movement that was appended, or the violated rule.</returns>
    public Result<StockMovement> Adjust(
        int delta,
        string? reasonCode,
        Guid recordedBy,
        string? idempotencyKey,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = ValidateAdjustment(delta, reasonCode, out var reason);
        if (error is not null)
        {
            return Result<StockMovement>.Failure(error);
        }

        var onHandAfter = OnHand + delta;
        if (onHandAfter < Reserved)
        {
            return Result<StockMovement>.Failure(InventoryErrors.BelowReserved(OnHand, Reserved));
        }

        if (onHandAfter > MaxOnHand)
        {
            return Result<StockMovement>.Failure(InventoryErrors.AboveMaximum);
        }

        OnHand = onHandAfter;
        MarkUpdated(timeProvider);

        var movement = StockMovement.Record(Id, delta, reason, onHandAfter, recordedBy, idempotencyKey, timeProvider);
        _movements.Add(movement);
        AddDomainEvent(StockAdjusted.For(this, movement));

        return Result<StockMovement>.Success(movement);
    }

    /// <summary>Holds units for an order in progress (BR-INV-005). Never more than <see cref="Available"/>.</summary>
    /// <param name="quantity">Units to reserve; positive.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Reserve(int quantity, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (quantity <= 0)
        {
            return Result.Failure(InventoryErrors.QuantityMustBePositive);
        }

        if (quantity > Available)
        {
            return Result.Failure(InventoryErrors.InsufficientAvailable(quantity, Available));
        }

        Reserved += quantity;
        MarkUpdated(timeProvider);
        AddDomainEvent(StockReserved.For(this, quantity));

        return Result.Success();
    }

    /// <summary>Returns held units to the available stock (BR-INV-006). Never more than <see cref="Reserved"/>.</summary>
    /// <param name="quantity">Units to release; positive.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Release(int quantity, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (quantity <= 0)
        {
            return Result.Failure(InventoryErrors.QuantityMustBePositive);
        }

        if (quantity > Reserved)
        {
            return Result.Failure(InventoryErrors.ReleaseExceedsReserved(quantity, Reserved));
        }

        Reserved -= quantity;
        MarkUpdated(timeProvider);
        AddDomainEvent(StockReleased.For(this, quantity));

        return Result.Success();
    }

    private static Error? ValidateAdjustment(int delta, string? reasonCode, out string reason)
    {
        reason = string.Empty;

        if (delta == 0)
        {
            return InventoryErrors.AdjustmentDeltaZero;
        }

        if (Math.Abs((long)delta) > MaxAdjustmentMagnitude)
        {
            return InventoryErrors.AdjustmentDeltaRange;
        }

        var normalized = StockMovement.NormalizeReason(reasonCode);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        reason = normalized.Value;
        return null;
    }
}
