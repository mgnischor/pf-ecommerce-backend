using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>
/// Cancels an order (BR-ORD-004). Errors come in a fixed order: an order that is not the caller's does not exist (404),
/// then a retry is recognized, then the version is checked (412), then the state machine decides (409), then the
/// request is validated (422). Idempotent: the order keeps the client's <c>Idempotency-Key</c>, so a retry of the same
/// cancellation answers with the cancelled order and cancels nothing twice, while the same key with a different
/// cancellation is rejected. The replay check runs before the version check because a retry necessarily carries the
/// version the original request read, which the original already advanced.
/// </summary>
internal sealed class CancelOrderHandler(IOrderRepository orders, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cancelled order, or the violated rule.</returns>
    public async Task<Result<OrderView>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null || order.CustomerId != command.CustomerId)
        {
            return Result<OrderView>.Failure(OrderErrors.NotFound);
        }

        if (string.Equals(order.CancellationKey, command.IdempotencyKey, StringComparison.Ordinal))
        {
            return order.WasCancelledFor(command.ReasonCode, command.Note)
                ? Result<OrderView>.Success(order.ToView())
                : Result<OrderView>.Failure(OrderErrors.IdempotencyKeyReused);
        }

        if (command.ExpectedVersion != order.Version)
        {
            return Result<OrderView>.Failure(OrderErrors.VersionMismatch);
        }

        var cancelled = order.Cancel(command.ReasonCode, command.Note, command.IdempotencyKey, timeProvider);
        if (cancelled.IsFailure)
        {
            return Result<OrderView>.Failure(cancelled.Error);
        }

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrderView>.Success(order.ToView());
    }
}
