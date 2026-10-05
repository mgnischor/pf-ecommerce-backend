using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Ordering.API.Contracts;
using Portfolio.Ordering.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Ordering.API.Controllers;

/// <summary>Order lifecycle after checkout. Customers only reach their own orders; others answer <c>404</c>.</summary>
[Route("api/v1/orders")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class OrdersController(ListOrdersHandler list, GetOrderHandler get, CancelOrderHandler cancel)
    : ApiControllerBase
{
    /// <summary>Lists the caller's orders, newest first unless <paramref name="sort"/> says otherwise.</summary>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page; valid only with the same filters and sort.</param>
    /// <param name="status">Filter by status.</param>
    /// <param name="createdFrom">Only orders placed on or after this calendar date (<c>YYYY-MM-DD</c>, UTC).</param>
    /// <param name="sort">One of <c>placedAt</c> or <c>total</c>, optionally prefixed with <c>-</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<CursorPage<OrderSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, 100)] int limit = ListOrdersHandler.DefaultLimit,
        [FromQuery, StringLength(512)] string? cursor = null,
        [FromQuery] OrderStatusContract? status = null,
        [FromQuery] DateOnly? createdFrom = null,
        [
            FromQuery,
            RegularExpression(
                ContractPatterns.SortExpression,
                MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
            )
        ]
            string? sort = null,
        CancellationToken cancellationToken = default
    )
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var query = new ListOrdersQuery(
            customer,
            limit,
            cursor,
            sort,
            status is { } contract ? ToView(contract) : null,
            createdFrom
        );
        var result = await list.HandleAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        var page = result.Value;
        return Ok(
            new CursorPage<OrderSummaryResponse>([.. page.Items.Select(ToSummary)], page.NextCursor, page.HasMore)
        );
    }

    /// <summary>Gets an order with its price snapshot and the actions its state currently allows.</summary>
    /// <param name="orderId">Order identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        return Respond(await get.HandleAsync(new GetOrderQuery(customer, orderId), cancellationToken));
    }

    /// <summary>Cancels an order when its state machine allows it (before it is shipped).</summary>
    /// <param name="orderId">Order identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Reason for the cancellation.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{orderId:guid}/cancellation")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Cancel(
        Guid orderId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken
    )
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new CancelOrderCommand(
            customer,
            orderId,
            idempotencyKey,
            ETags.TryParseVersion(ifMatch, out var version) ? version : null,
            request.ReasonCode,
            request.Note
        );

        return Respond(await cancel.HandleAsync(command, cancellationToken));
    }

    private IActionResult Respond(Result<OrderView> result)
    {
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Ok(ToResponse(result.Value));
    }

    private static OrderResponse ToResponse(OrderView view) =>
        new(
            view.Id,
            view.Number,
            ToContract(view.Status),
            [.. view.Items.Select(ToResponse)],
            MoneyContract.ToResponse(view.TotalAmount, view.Currency),
            view.PlacedAt.UtcDateTime,
            view.Version,
            view.AllowedActions
        );

    private static OrderItemResponse ToResponse(OrderItemView item) =>
        new(
            item.ProductId,
            item.Sku,
            item.Name,
            item.Quantity,
            MoneyContract.ToResponse(item.UnitPriceAmount, item.Currency),
            MoneyContract.ToResponse(item.LineTotalAmount, item.Currency)
        );

    private static OrderSummaryResponse ToSummary(OrderSummaryView view) =>
        new(
            view.Id,
            view.Number,
            ToContract(view.Status),
            MoneyContract.ToResponse(view.TotalAmount, view.Currency),
            view.PlacedAt.UtcDateTime
        );

    private static OrderStatusContract ToContract(OrderStatusView status) =>
        status switch
        {
            OrderStatusView.AwaitingPayment => OrderStatusContract.AwaitingPayment,
            OrderStatusView.Paid => OrderStatusContract.Paid,
            OrderStatusView.Shipped => OrderStatusContract.Shipped,
            OrderStatusView.Delivered => OrderStatusContract.Delivered,
            OrderStatusView.Cancelled => OrderStatusContract.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status."),
        };

    private static OrderStatusView ToView(OrderStatusContract status) =>
        status switch
        {
            OrderStatusContract.AwaitingPayment => OrderStatusView.AwaitingPayment,
            OrderStatusContract.Paid => OrderStatusView.Paid,
            OrderStatusContract.Shipped => OrderStatusView.Shipped,
            OrderStatusContract.Delivered => OrderStatusView.Delivered,
            OrderStatusContract.Cancelled => OrderStatusView.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status."),
        };
}
