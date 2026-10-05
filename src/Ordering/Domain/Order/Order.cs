using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Domain;

/// <summary>
/// An order after checkout (aggregate root). Enforces on every change: BR-ORD-001 (what an order is made of),
/// BR-ORD-002 (the price snapshot and the server-computed total), BR-ORD-003 (lifecycle), BR-ORD-004 (cancellation),
/// BR-ORD-005 (a number the customer can quote). Lines refer to products by identifier only and carry the prices as at
/// purchase time, so the order never changes when the catalog does.
/// </summary>
internal sealed class Order : AggregateRoot
{
    /// <summary>Largest number of lines in one order (BR-ORD-001).</summary>
    public const int MaxItems = 50;

    /// <summary>Largest quantity of one line (BR-ORD-001).</summary>
    public const int MaxLineQuantity = 99;

    /// <summary>Shortest cancellation reason code (BR-ORD-004).</summary>
    public const int MinReasonLength = 2;

    /// <summary>Longest cancellation reason code (BR-ORD-004).</summary>
    public const int MaxReasonLength = 32;

    /// <summary>Longest cancellation note (BR-ORD-004).</summary>
    public const int MaxNoteLength = 500;

    /// <summary>Longest order number (BR-ORD-005).</summary>
    public const int MaxNumberLength = 32;

    /// <summary>Longest idempotency key (BR-ORD-004).</summary>
    public const int MaxIdempotencyKeyLength = 64;

    private readonly List<OrderItem> _items = [];

    /// <summary>Human-readable order number, unique (BR-ORD-005).</summary>
    public string Number { get; private set; }

    /// <summary>The checkout this order was placed from; at most one order per checkout (BR-ORD-001).</summary>
    public Guid CheckoutId { get; private set; }

    /// <summary>The customer (account) that owns the order.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Lifecycle status.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>Sum of the line totals, computed when the order was placed (BR-ORD-002).</summary>
    public Money Total { get; private set; }

    /// <summary>UTC instant the order was placed.</summary>
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>UTC instant the order was paid.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>UTC instant the order was handed to the carrier.</summary>
    public DateTimeOffset? ShippedAt { get; private set; }

    /// <summary>UTC instant the order was delivered.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>UTC instant the order was cancelled.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Machine-readable cancellation reason, such as <c>changedMind</c>.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Optional free text of the cancellation, plain text.</summary>
    public string? CancellationNote { get; private set; }

    /// <summary>The client's <c>Idempotency-Key</c> of the request that cancelled the order (BR-ORD-004).</summary>
    public string? CancellationKey { get; private set; }

    /// <summary>The lines of the order.</summary>
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private Order() { }
#pragma warning restore CS8618

    private Order(Guid id, string number, Guid checkoutId, Guid customerId, Money total, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        Number = number;
        CheckoutId = checkoutId;
        CustomerId = customerId;
        Status = OrderStatus.AwaitingPayment;
        Total = total;
        PlacedAt = CreatedAt;
    }

    /// <summary>
    /// Places an order from the lines a checkout priced (BR-ORD-001). The total is computed here from the lines, never
    /// taken from the caller (BR-ORD-002). Raises <see cref="OrderPlaced"/>.
    /// </summary>
    /// <param name="checkoutId">The checkout the order comes from.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="number">The order number, unique (BR-ORD-005).</param>
    /// <param name="lines">The lines, priced as at purchase time.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static Result<Order> Place(
        Guid checkoutId,
        Guid customerId,
        string? number,
        IReadOnlyCollection<OrderLine>? lines,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = Validate(checkoutId, customerId, number, lines);
        if (error is not null)
        {
            return Result<Order>.Failure(error);
        }

        // Validate guarantees a number and at least one line; the fallbacks only satisfy the compiler.
        var drafts = lines ?? [];
        var total = drafts.Aggregate(
            Money.Zero(drafts.First().UnitPrice.Currency),
            (sum, line) => sum.Add(line.UnitPrice.Multiply(line.Quantity))
        );
        var order = new Order(
            NewId(timeProvider),
            number?.Trim() ?? string.Empty,
            checkoutId,
            customerId,
            total,
            timeProvider
        );
        order._items.AddRange(drafts.Select(line => OrderItem.From(order.Id, line, timeProvider)));
        order.AddDomainEvent(OrderPlaced.For(order));

        return Result<Order>.Success(order);
    }

    /// <summary>Records the payment (BR-ORD-003: AwaitingPayment → Paid). Raises <see cref="OrderPaid"/>.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkPaid(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var moved = MoveTo(OrderStatus.Paid, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        PaidAt = UpdatedAt;
        AddDomainEvent(OrderPaid.For(this));
        return Result.Success();
    }

    /// <summary>Records the hand-over to the carrier (BR-ORD-003: Paid → Shipped). Raises <see cref="OrderShipped"/>.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkShipped(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var moved = MoveTo(OrderStatus.Shipped, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        ShippedAt = UpdatedAt;
        AddDomainEvent(OrderShipped.For(this));
        return Result.Success();
    }

    /// <summary>Records the delivery (BR-ORD-003: Shipped → Delivered). Raises <see cref="OrderDelivered"/>.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkDelivered(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var moved = MoveTo(OrderStatus.Delivered, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        DeliveredAt = UpdatedAt;
        AddDomainEvent(OrderDelivered.For(this));
        return Result.Success();
    }

    /// <summary>
    /// Cancels the order (BR-ORD-004: AwaitingPayment | Paid → Cancelled). Once it was handed to the carrier it can no
    /// longer be cancelled. Raises <see cref="OrderCancelled"/>, which says whether the order had been paid, so that the
    /// payment can be refunded.
    /// </summary>
    /// <param name="reasonCode">Machine-readable reason (2–32 letters, digits or underscores, starting with a letter).</param>
    /// <param name="note">Optional free text, up to 500 characters, kept as plain text. Blank is stored as absent.</param>
    /// <param name="idempotencyKey">The client's <c>Idempotency-Key</c>, kept so a retry is recognized.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Cancel(string? reasonCode, string? note, string idempotencyKey, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.Paid))
        {
            return Result.Failure(OrderErrors.InvalidStatusTransition(Status, OrderStatus.Cancelled));
        }

        var error = NormalizeCancellation(reasonCode, note, out var reason, out var normalizedNote);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        var wasPaid = Status == OrderStatus.Paid;
        var moved = MoveTo(OrderStatus.Cancelled, timeProvider);
        if (moved.IsFailure)
        {
            return moved;
        }

        CancelledAt = UpdatedAt;
        CancellationReason = reason;
        CancellationNote = normalizedNote;
        CancellationKey = idempotencyKey;
        AddDomainEvent(OrderCancelled.For(this, wasPaid));
        return Result.Success();
    }

    /// <summary>
    /// Whether the order was cancelled for the reason and with the note a request carries, so a retry of that request
    /// (same <c>Idempotency-Key</c>) is recognized and a different one is not (BR-ORD-004).
    /// </summary>
    /// <param name="reasonCode">Requested reason.</param>
    /// <param name="note">Requested note.</param>
    public bool WasCancelledFor(string? reasonCode, string? note)
    {
        _ = NormalizeCancellation(reasonCode, note, out var reason, out var normalizedNote);

        return Status == OrderStatus.Cancelled
            && string.Equals(CancellationReason, reason, StringComparison.Ordinal)
            && string.Equals(CancellationNote, normalizedNote, StringComparison.Ordinal);
    }

    private Result MoveTo(OrderStatus target, TimeProvider timeProvider)
    {
        if (!IsValidTransition(Status, target))
        {
            return Result.Failure(OrderErrors.InvalidStatusTransition(Status, target));
        }

        Status = target;
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    private static bool IsValidTransition(OrderStatus from, OrderStatus to) =>
        (from, to) switch
        {
            (OrderStatus.AwaitingPayment, OrderStatus.Paid) => true,
            (OrderStatus.Paid, OrderStatus.Shipped) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            (OrderStatus.AwaitingPayment, OrderStatus.Cancelled) => true,
            (OrderStatus.Paid, OrderStatus.Cancelled) => true,
            _ => false,
        };

    private static Error? Validate(
        Guid checkoutId,
        Guid customerId,
        string? number,
        IReadOnlyCollection<OrderLine>? lines
    )
    {
        if (checkoutId == Guid.Empty || customerId == Guid.Empty)
        {
            return OrderErrors.CustomerRequired;
        }

        if (string.IsNullOrWhiteSpace(number) || number.Trim().Length > MaxNumberLength)
        {
            return OrderErrors.NumberRequired;
        }

        if (lines is null || lines.Count == 0)
        {
            return OrderErrors.NoItems;
        }

        if (lines.Count > MaxItems)
        {
            return OrderErrors.TooManyItems;
        }

        return lines.Select(ValidateLine).FirstOrDefault(error => error is not null)
            ?? (
                lines.Select(line => line.UnitPrice.Currency).Distinct(StringComparer.Ordinal).Count() > 1
                    ? OrderErrors.CurrencyMixed
                    : null
            );
    }

    private static Error? ValidateLine(OrderLine line)
    {
        if (line is null)
        {
            return OrderErrors.ItemInvalid;
        }

        if (
            line.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(line.Sku)
            || line.Sku.Trim().Length > OrderItem.MaxSkuLength
            || string.IsNullOrWhiteSpace(line.Name)
            || line.Name.Trim().Length > OrderItem.MaxNameLength
            || line.UnitPrice is null
            || !line.UnitPrice.IsPositive
        )
        {
            return OrderErrors.ItemInvalid;
        }

        return line.Quantity is < 1 or > MaxLineQuantity ? OrderErrors.ItemQuantityInvalid : null;
    }

    private static Error? NormalizeCancellation(
        string? reasonCode,
        string? note,
        out string? reason,
        out string? normalizedNote
    )
    {
        reason = reasonCode?.Trim();
        normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            return OrderErrors.ReasonRequired;
        }

        if (!IsValidReason(reason))
        {
            return OrderErrors.ReasonInvalid;
        }

        return normalizedNote is { Length: > MaxNoteLength } ? OrderErrors.NoteTooLong : null;
    }

    private static bool IsValidReason(string reason) =>
        reason.Length is >= MinReasonLength and <= MaxReasonLength
        && char.IsAsciiLetter(reason[0])
        && reason.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
