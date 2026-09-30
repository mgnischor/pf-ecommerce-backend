using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Ordering.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Ordering.API.Controllers;

/// <summary>Order lifecycle after checkout. Customers only reach their own orders; others answer <c>404</c>.</summary>
[Route("api/v1/orders")]
[Authorize(Roles = ApiRoles.CustomerOrStaff)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class OrdersController : ApiControllerBase
{
    /// <summary>Lists the caller's orders, newest first unless <paramref name="sort"/> says otherwise.</summary>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page.</param>
    /// <param name="status">Filter by status.</param>
    /// <param name="createdFrom">Only orders placed on or after this calendar date (<c>YYYY-MM-DD</c>).</param>
    /// <param name="sort">One of <c>placedAt</c> or <c>total</c>, optionally prefixed with <c>-</c>.</param>
    [HttpGet]
    [ProducesResponseType<CursorPage<OrderSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public IActionResult List(
        [FromQuery, Range(1, 100)] int limit = 20,
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
            string? sort = null
    ) => NotImplementedYet();

    /// <summary>Gets an order with its price snapshot and the actions its state currently allows.</summary>
    /// <param name="orderId">Order identifier.</param>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(Guid orderId) => NotImplementedYet();

    /// <summary>Cancels an order when its state machine allows it.</summary>
    /// <param name="orderId">Order identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Reason for the cancellation.</param>
    [HttpPost("{orderId:guid}/cancellation")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public IActionResult Cancel(
        Guid orderId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] CancelOrderRequest request
    ) => NotImplementedYet();
}
