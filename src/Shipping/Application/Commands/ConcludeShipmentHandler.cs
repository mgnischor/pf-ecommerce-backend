using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Application;

/// <summary>
/// Records how a shipment in transit ended (BR-SHP-002: InTransit → Delivered | Failed). Idempotent: the carrier
/// repeating an outcome the shipment already has (the same one, with the same failure reason) is answered with the
/// shipment as it is and raises no second event, while a different outcome after the shipment ended is a conflict.
/// </summary>
internal sealed class ConcludeShipmentHandler(
    IShipmentRepository shipments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The shipment after the change, or the violated rule.</returns>
    public async Task<Result<ShipmentView>> HandleAsync(
        ConcludeShipmentCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await shipments.GetByIdAsync(command.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result<ShipmentView>.Failure(ShipmentErrors.NotFound);
        }

        if (shipment.HasConcludedWith(command.Outcome == ShipmentOutcome.Delivered, command.FailureReasonCode))
        {
            return Result<ShipmentView>.Success(shipment.ToView());
        }

        var concluded =
            command.Outcome == ShipmentOutcome.Delivered
                ? shipment.MarkDelivered(timeProvider)
                : shipment.MarkFailed(command.FailureReasonCode, timeProvider);
        if (concluded.IsFailure)
        {
            return Result<ShipmentView>.Failure(concluded.Error);
        }

        shipments.Update(shipment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ShipmentView>.Success(shipment.ToView());
    }
}
