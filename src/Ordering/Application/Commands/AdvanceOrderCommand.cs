namespace Portfolio.Ordering.Application;

/// <summary>
/// Reaction to another context reporting a step of an order's life (BR-ORD-003): the payment was captured, the carrier
/// took the shipment, the shipment was delivered. Ordering's own reading of the event; it never references the
/// publisher's types.
/// </summary>
/// <param name="MessageId">Identifier of the event, which the inbox deduplicates on.</param>
/// <param name="Consumer">Stable name of the consumer, so two consumers may handle the same message.</param>
/// <param name="OrderId">The order the event is about.</param>
/// <param name="Milestone">The step reported.</param>
internal sealed record AdvanceOrderCommand(Guid MessageId, string Consumer, Guid OrderId, OrderMilestone Milestone);
