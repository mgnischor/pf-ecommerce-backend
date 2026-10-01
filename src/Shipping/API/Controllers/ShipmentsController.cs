using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.Shipping.API.Contracts;

namespace Portfolio.Shipping.API.Controllers;

/// <summary>Fulfillment and tracking of an order.</summary>
[Route("api/v1/orders/{orderId:guid}/shipments")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class ShipmentsController : ApiControllerBase
{
    /// <summary>Lists the shipments of an order. An order of another customer answers <c>404</c>.</summary>
    /// <param name="orderId">Order identifier.</param>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page.</param>
    [HttpGet]
    [ProducesResponseType<CursorPage<ShipmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult List(
        Guid orderId,
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, StringLength(512)] string? cursor = null
    ) => NotImplementedYet();
}
