using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Application;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>
/// Consumes the Ordering's order events (<c>ordering.*</c>): remembers whose an order is, starts the shipment when the
/// order is paid, and cancels it when the order is cancelled before the carrier took it (BR-SHP-001, BR-SHP-004,
/// BR-SHP-005). The queue belongs to this consumer, so Shipping keeps receiving events while Ordering is down or
/// redeployed. An order event of a kind Shipping does not use is acknowledged and ignored.
/// </summary>
internal sealed class OrderEventsConsumer : JsonMessageConsumer<OrderEventMessage>
{
    private const string OrderPlaced = "ordering.order-placed";
    private const string OrderPaid = "ordering.order-paid";
    private const string OrderCancelled = "ordering.order-cancelled";

    /// <inheritdoc />
    public override string Name => SyncOrderHandler.ConsumerName;

    /// <inheritdoc />
    public override string BindingKey => "ordering.*";

    /// <inheritdoc />
    protected override async Task<bool> HandleAsync(
        ReceivedMessage message,
        OrderEventMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var kind = message.RoutingKey switch
        {
            OrderPlaced => OrderEventKind.Placed,
            OrderPaid => OrderEventKind.Paid,
            OrderCancelled => OrderEventKind.Cancelled,
            _ => (OrderEventKind?)null,
        };
        if (kind is null)
        {
            return true;
        }

        var handler = services.GetRequiredService<SyncOrderHandler>();
        var result = await handler.HandleAsync(
            new SyncOrderCommand(
                message.MessageId,
                kind.Value,
                payload.AggregateId,
                payload.CustomerId,
                payload.Number
            ),
            cancellationToken
        );

        // Nothing retried later can fix a malformed event, or a cancellation that finds the carrier already holding the
        // shipment: both go to the dead-letter queue, where a person decides.
        return result.IsFailure ? throw FailureOf(result.Error) : result.Value;
    }

    private static PoisonMessageException FailureOf(Error error) =>
        new(
            error.Type == ErrorType.Conflict
                ? "The order was cancelled after the carrier took its shipment."
                : "The order event lacks data Shipping needs."
        );
}
