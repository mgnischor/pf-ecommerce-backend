using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Checkout.API.Contracts;
using Portfolio.SharedKernel.API;

namespace Portfolio.Checkout.API.Controllers;

/// <summary>Conversion of a cart into a confirmed purchase intent.</summary>
[Route("api/v1")]
[Authorize(Roles = ApiRoles.Customer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class CheckoutsController : ApiControllerBase
{
    /// <summary>
    /// Starts the checkout of a cart (reserve stock, authorize payment, place the order). Asynchronous:
    /// answers <c>202</c> with a <c>Location</c> header pointing to the checkout resource.
    /// </summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result and never charges twice.</param>
    /// <param name="request">Shipping and payment choices; totals are computed by the server.</param>
    [HttpPost("carts/{cartId:guid}/checkout")]
    [ProducesResponseType<CheckoutResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public IActionResult Start(
        Guid cartId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromBody] StartCheckoutRequest request
    ) => NotImplementedYet();

    /// <summary>Gets the state of a checkout so the client can poll until it completes or fails.</summary>
    /// <param name="checkoutId">Checkout identifier.</param>
    [HttpGet("checkouts/{checkoutId:guid}")]
    [ProducesResponseType<CheckoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(Guid checkoutId) => NotImplementedYet();
}
