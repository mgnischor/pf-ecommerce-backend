using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>
/// Moves an order forward when another context reports it (BR-ORD-003). Delivery is at least once, so the handler is
/// idempotent twice over: the inbox row of the message is committed with the change (a redelivery finds it and does
/// nothing), and a step the order has already reached or passed (a second shipment event, a payment reported twice
/// by two messages) is accepted and changes nothing. A step the lifecycle does not allow, such as a payment on a
/// cancelled order, is a conflict the consumer dead-letters, because only a person can decide what to do with it.
/// </summary>
internal sealed class AdvanceOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IInbox inbox,
    TimeProvider timeProvider
)
{
    /// <summary>The event names an order that does not exist. Nothing retried later can fix it.</summary>
    public static Error UnknownOrder => Error.NotFound("ORDER_NOT_FOUND");

    /// <summary>Executes the command.</summary>
    /// <param name="command">The event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the message was handled now, <c>false</c> when it was a duplicate, or why it cannot be handled.</returns>
    public async Task<Result<bool>> HandleAsync(AdvanceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await inbox.TryBeginAsync(command.MessageId, command.Consumer, cancellationToken))
        {
            return Result<bool>.Success(false);
        }

        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<bool>.Failure(UnknownOrder);
        }

        var target = command.Milestone switch
        {
            OrderMilestone.Paid => OrderStatus.Paid,
            OrderMilestone.Shipped => OrderStatus.Shipped,
            _ => OrderStatus.Delivered,
        };

        if (HasReached(order.Status, target))
        {
            // Nothing to apply, but the message is handled: its inbox row is committed.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<bool>.Success(true);
        }

        var moved = command.Milestone switch
        {
            OrderMilestone.Paid => order.MarkPaid(timeProvider),
            OrderMilestone.Shipped => order.MarkShipped(timeProvider),
            _ => order.MarkDelivered(timeProvider),
        };
        if (moved.IsFailure)
        {
            return Result<bool>.Failure(moved.Error);
        }

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    // The lifecycle only moves forward along AwaitingPayment → Paid → Shipped → Delivered; a cancelled order has
    // reached none of them.
    private static bool HasReached(OrderStatus current, OrderStatus target) =>
        current != OrderStatus.Cancelled && current >= target;
}
