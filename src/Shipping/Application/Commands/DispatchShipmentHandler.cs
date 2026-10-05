using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Application;

/// <summary>
/// Records that the carrier took a shipment (BR-SHP-002: Preparing → InTransit). Idempotent: the carrier repeating the
/// same hand-over (same carrier, tracking code and estimate) is answered with the shipment as it is and raises no second
/// event, while a different hand-over of a shipment the carrier already holds is a conflict.
/// </summary>
internal sealed class DispatchShipmentHandler(
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
        DispatchShipmentCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var shipment = await shipments.GetByIdAsync(command.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result<ShipmentView>.Failure(ShipmentErrors.NotFound);
        }

        if (shipment.WasDispatchedWith(command.Carrier, command.TrackingCode, command.EstimatedDeliveryDate))
        {
            return Result<ShipmentView>.Success(shipment.ToView());
        }

        var dispatched = shipment.Dispatch(
            command.Carrier,
            command.TrackingCode,
            command.EstimatedDeliveryDate,
            timeProvider
        );
        if (dispatched.IsFailure)
        {
            return Result<ShipmentView>.Failure(dispatched.Error);
        }

        shipments.Update(shipment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ShipmentView>.Success(shipment.ToView());
    }
}
