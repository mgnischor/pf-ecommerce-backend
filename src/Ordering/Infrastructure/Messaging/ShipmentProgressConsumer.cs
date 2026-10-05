using Portfolio.Ordering.Application;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>
/// Moves an order forward when the Shipping context reports its shipment (BR-ORD-003). One class serves both events;
/// each queue belongs to one consumer, so Ordering keeps receiving events while Shipping is down or redeployed.
/// </summary>
/// <param name="name">Stable consumer name: the queue name and the inbox key.</param>
/// <param name="bindingKey">Routing key of the Shipping event.</param>
/// <param name="milestone">The step of the order's life the event reports.</param>
internal abstract class ShipmentProgressConsumer(string name, string bindingKey, OrderMilestone milestone)
    : JsonMessageConsumer<ShipmentEventMessage>
{
    /// <inheritdoc />
    public sealed override string Name => name;

    /// <inheritdoc />
    public sealed override string BindingKey => bindingKey;

    /// <inheritdoc />
    protected sealed override async Task<bool> HandleAsync(
        ReceivedMessage message,
        ShipmentEventMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var handler = services.GetRequiredService<AdvanceOrderHandler>();
        var result = await handler.HandleAsync(
            new AdvanceOrderCommand(message.MessageId, name, payload.OrderId, milestone),
            cancellationToken
        );

        // An order the event names but Ordering does not know, or a step its lifecycle does not allow, cannot be fixed
        // by retrying: the message goes to the dead-letter queue, where a person decides.
        return result.IsFailure
            ? throw new PoisonMessageException("The shipment event cannot be applied to its order.")
            : result.Value;
    }
}
