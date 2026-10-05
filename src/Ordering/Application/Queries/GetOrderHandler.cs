using Portfolio.Ordering.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.Application;

/// <summary>
/// Reads one order with its price snapshot (BR-ORD-006). An order of another customer answers exactly like one that does
/// not exist, so its existence is not revealed: customers only reach their own orders, whatever their access level.
/// </summary>
internal sealed class GetOrderHandler(IOrderRepository orders)
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">Validated request data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The order, or the reason it cannot be read.</returns>
    public async Task<Result<OrderView>> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await orders.GetByIdAsync(query.OrderId, cancellationToken);

        return order is null || order.CustomerId != query.CustomerId
            ? Result<OrderView>.Failure(OrderErrors.NotFound)
            : Result<OrderView>.Success(order.ToView());
    }
}
