using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>
/// Places the order of a checkout (BR-ORD-001). Idempotent by checkout: a checkout produces at most one order, so
/// placing it again answers with the order that already exists instead of creating a second one (a unique index on the
/// checkout closes the race between two concurrent placements). The order number is reserved from a database sequence
/// (BR-ORD-005), the total is computed from the lines (BR-ORD-002), and <see cref="OrderPlaced"/> reaches the outbox in
/// the same transaction as the order.
/// </summary>
internal sealed class PlaceOrderHandler(IOrderRepository orders, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">The checkout's data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The order, or the violated rule.</returns>
    public async Task<Result<OrderView>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await orders.FindByCheckoutAsync(command.CheckoutId, cancellationToken);
        if (existing is not null)
        {
            return Result<OrderView>.Success(existing.ToView());
        }

        var lines = new List<OrderLine>();
        foreach (var line in command.Lines ?? [])
        {
            var price = Money.Create(line.UnitPrice, line.Currency);
            if (price.IsFailure)
            {
                return Result<OrderView>.Failure(price.Error);
            }

            lines.Add(
                new OrderLine(
                    line.ProductId,
                    line.Sku ?? string.Empty,
                    line.Name ?? string.Empty,
                    line.Quantity,
                    price.Value
                )
            );
        }

        var number = await orders.NextNumberAsync(timeProvider.GetUtcNow(), cancellationToken);
        var order = Order.Place(command.CheckoutId, command.CustomerId, number, lines, timeProvider);
        if (order.IsFailure)
        {
            return Result<OrderView>.Failure(order.Error);
        }

        await orders.AddAsync(order.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrderView>.Success(order.Value.ToView());
    }
}
