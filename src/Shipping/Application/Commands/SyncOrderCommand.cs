namespace Portfolio.Shipping.Application;

/// <summary>
/// Reaction to one of the Ordering's order events. Shipping's own reading of the event; it never references Ordering's types.
/// </summary>
/// <param name="MessageId">Identifier of the event, which the inbox deduplicates on.</param>
/// <param name="Kind">Which event it is.</param>
/// <param name="OrderId">The order (the event's aggregate identifier).</param>
/// <param name="CustomerId">The order's owner.</param>
/// <param name="Number">The order number.</param>
internal sealed record SyncOrderCommand(
    Guid MessageId,
    OrderEventKind Kind,
    Guid OrderId,
    Guid CustomerId,
    string? Number
);
