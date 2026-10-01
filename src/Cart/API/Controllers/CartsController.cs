using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Cart.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;

namespace Portfolio.Cart.API.Controllers;

/// <summary>The shopper's pre-purchase item selection. Every cart belongs to the authenticated customer.</summary>
[Route("api/v1/carts")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class CartsController : ApiControllerBase
{
    /// <summary>Opens a cart for the authenticated customer.</summary>
    /// <param name="request">Cart settings.</param>
    [HttpPost]
    [ProducesResponseType<CartResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult Create([FromBody] CreateCartRequest request) => NotImplementedYet();

    /// <summary>Gets a cart with server-computed prices. A cart of another customer answers <c>404</c>.</summary>
    /// <param name="cartId">Cart identifier.</param>
    [HttpGet("{cartId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(Guid cartId) => NotImplementedYet();

    /// <summary>Adds a product to the cart, or increases the quantity of an existing line.</summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="request">Product and quantity.</param>
    [HttpPost("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult AddItem(Guid cartId, [FromBody] AddCartItemRequest request) => NotImplementedYet();

    /// <summary>Removes a line from the cart and returns the resulting cart.</summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="itemId">Line identifier.</param>
    [HttpDelete("{cartId:guid}/items/{itemId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public IActionResult RemoveItem(Guid cartId, Guid itemId) => NotImplementedYet();
}
