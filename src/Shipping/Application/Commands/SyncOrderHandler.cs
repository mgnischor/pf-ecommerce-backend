using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Application;

/// <summary>
/// Reacts to the Ordering's order events (BR-SHP-001, BR-SHP-004, BR-SHP-005): remembers whose an order is, starts the
/// shipment when the order is paid, and cancels it when the order is cancelled before the carrier took it.
/// Delivery is at least once, so the handler is idempotent twice over: the inbox row of the message is committed with the
/// change (a redelivery finds it and does nothing), and what already exists is left alone (one record per order, one
/// shipment per order, a shipment already cancelled stays cancelled). A cancellation that finds the carrier already
/// holding the shipment cannot be applied here: it is a conflict the consumer dead-letters, because only a person can
/// bring the parcel back.
/// </summary>
internal sealed class SyncOrderHandler(
    IOrderReferenceRepository references,
    IShipmentRepository shipments,
    IUnitOfWork unitOfWork,
    IInbox inbox,
    TimeProvider timeProvider
)
{
    /// <summary>Stable consumer name: the inbox key, the queue name and the telemetry label.</summary>
    public const string ConsumerName = "shipping.sync-orders";

    /// <summary>Executes the command.</summary>
    /// <param name="command">The event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the message was handled now, <c>false</c> when it was a duplicate, or why it cannot be handled.</returns>
    public async Task<Result<bool>> HandleAsync(SyncOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OrderId == Guid.Empty)
        {
            return Result<bool>.Failure(ShipmentErrors.MalformedOrderEvent);
        }

        if (!await inbox.TryBeginAsync(command.MessageId, ConsumerName, cancellationToken))
        {
            return Result<bool>.Success(false);
        }

        var applied = command.Kind switch
        {
            OrderEventKind.Placed => await RememberOrderAsync(command, cancellationToken),
            OrderEventKind.Paid => await PrepareShipmentAsync(command, cancellationToken),
            _ => await CancelShipmentAsync(command, cancellationToken),
        };
        if (applied.IsFailure)
        {
            return Result<bool>.Failure(applied.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    private async Task<Result> RememberOrderAsync(SyncOrderCommand command, CancellationToken cancellationToken)
    {
        if (await references.FindAsync(command.OrderId, cancellationToken) is not null)
        {
            return Result.Success();
        }

        var reference = OrderReference.Record(command.OrderId, command.CustomerId, command.Number, timeProvider);
        if (reference.IsFailure)
        {
            return Result.Failure(reference.Error);
        }

        await references.AddAsync(reference.Value, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> PrepareShipmentAsync(SyncOrderCommand command, CancellationToken cancellationToken)
    {
        // The paid event repeats what the placed one said, so the order is known even if the placed event is behind.
        var remembered = await RememberOrderAsync(command, cancellationToken);
        if (remembered.IsFailure)
        {
            return remembered;
        }

        if (await shipments.FindByOrderAsync(command.OrderId, cancellationToken) is not null)
        {
            return Result.Success();
        }

        var shipment = Shipment.Prepare(command.OrderId, command.CustomerId, timeProvider);
        if (shipment.IsFailure)
        {
            return Result.Failure(shipment.Error);
        }

        await shipments.AddAsync(shipment.Value, cancellationToken);
        return Result.Success();
    }

    private async Task<Result> CancelShipmentAsync(SyncOrderCommand command, CancellationToken cancellationToken)
    {
        var shipment = await shipments.FindByOrderAsync(command.OrderId, cancellationToken);
        if (shipment is null || shipment.Status == ShipmentStatus.Cancelled)
        {
            return Result.Success();
        }

        var cancelled = shipment.Cancel(timeProvider);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        shipments.Update(shipment);
        return Result.Success();
    }
}
