using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Cart.API.Contracts;
using Portfolio.Cart.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.API.Controllers;

/// <summary>The shopper's pre-purchase item selection. Every cart belongs to the authenticated customer.</summary>
[Route("api/v1/carts")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class CartsController(
    OpenCartHandler open,
    GetCartHandler get,
    AddCartItemHandler addItem,
    RemoveCartItemHandler removeItem
) : ApiControllerBase
{
    /// <summary>
    /// Opens a cart for the authenticated customer. A customer has one active cart: when it is already open in the same
    /// currency it is returned with <c>200</c> instead of creating a second one, which makes a retry safe.
    /// </summary>
    /// <param name="request">Cart settings.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CartResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreateCartRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var result = await open.HandleAsync(new OpenCartCommand(customer, request.Currency), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        var opened = result.Value;
        Response.Headers.ETag = ETags.ForVersion(opened.Cart.Version);
        var location = new Uri($"/api/v1/carts/{opened.Cart.Id}", UriKind.Relative);

        return opened.Created ? Created(location, ToResponse(opened.Cart)) : Ok(ToResponse(opened.Cart));
    }

    /// <summary>Gets a cart with server-computed prices. A cart of another customer answers <c>404</c>.</summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{cartId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid cartId, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        return Respond(await get.HandleAsync(new GetCartQuery(customer, cartId), cancellationToken));
    }

    /// <summary>Adds a product to the cart, or increases the quantity of an existing line.</summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="request">Product and quantity.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{cartId:guid}/items")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> AddItem(
        Guid cartId,
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken
    )
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new AddCartItemCommand(customer, cartId, request.ProductId, request.Quantity);
        return Respond(await addItem.HandleAsync(command, cancellationToken));
    }

    /// <summary>Removes a line from the cart and returns the resulting cart.</summary>
    /// <param name="cartId">Cart identifier.</param>
    /// <param name="itemId">Line identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpDelete("{cartId:guid}/items/{itemId:guid}")]
    [ProducesResponseType<CartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> RemoveItem(Guid cartId, Guid itemId, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } customer)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        return Respond(
            await removeItem.HandleAsync(new RemoveCartItemCommand(customer, cartId, itemId), cancellationToken)
        );
    }

    private IActionResult Respond(Result<CartView> result)
    {
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Ok(ToResponse(result.Value));
    }

    private static CartResponse ToResponse(CartView view) =>
        new(
            view.Id,
            ToContract(view.Status),
            [.. view.Items.Select(ToResponse)],
            MoneyContract.ToResponse(view.SubtotalAmount, view.Currency),
            view.Version,
            view.AllowedActions
        );

    private static CartItemResponse ToResponse(CartItemView item) =>
        new(
            item.Id,
            item.ProductId,
            item.Sku,
            item.Name,
            item.Quantity,
            MoneyContract.ToResponse(item.UnitPriceAmount, item.Currency),
            MoneyContract.ToResponse(item.LineTotalAmount, item.Currency)
        );

    private static CartStatusContract ToContract(CartStatusView status) =>
        status switch
        {
            CartStatusView.Active => CartStatusContract.Active,
            CartStatusView.CheckedOut => CartStatusContract.CheckedOut,
            CartStatusView.Expired => CartStatusContract.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown cart status."),
        };
}
