using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.SharedKernel.Domain;
using Portfolio.Shipping.API.Contracts;
using Portfolio.Shipping.Application;

namespace Portfolio.Shipping.API.Controllers;

/// <summary>Fulfillment and tracking of an order.</summary>
[Route("api/v1/orders/{orderId:guid}/shipments")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class ShipmentsController(ListShipmentsHandler list) : ApiControllerBase
{
    /// <summary>
    /// Lists the shipments of an order. An order of another customer answers <c>404</c> exactly like one that does not
    /// exist; an order of the caller that has no shipment yet answers an empty page.
    /// </summary>
    /// <param name="orderId">Order identifier.</param>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page; valid only for the same order.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<CursorPage<ShipmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> List(
        Guid orderId,
        [FromQuery, Range(1, 100)] int limit = ListShipmentsHandler.DefaultLimit,
        [FromQuery, StringLength(512)] string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var result = await list.HandleAsync(
            new ListShipmentsQuery(customer, orderId, limit, cursor),
            cancellationToken
        );
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        var page = result.Value;
        return Ok(new CursorPage<ShipmentResponse>([.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static ShipmentResponse ToResponse(ShipmentView view) =>
        new(
            view.Id,
            view.OrderId,
            ToContract(view.Status),
            view.Carrier,
            view.TrackingCode,
            view.EstimatedDeliveryDate
        );

    private static ShipmentStatusContract ToContract(ShipmentStatusView status) =>
        status switch
        {
            ShipmentStatusView.Preparing => ShipmentStatusContract.Preparing,
            ShipmentStatusView.InTransit => ShipmentStatusContract.InTransit,
            ShipmentStatusView.Delivered => ShipmentStatusContract.Delivered,
            ShipmentStatusView.Failed => ShipmentStatusContract.Failed,
            ShipmentStatusView.Cancelled => ShipmentStatusContract.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown shipment status."),
        };
}
