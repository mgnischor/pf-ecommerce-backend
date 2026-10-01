using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Inventory.API.Contracts;
using Portfolio.Inventory.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Inventory.API.Controllers;

/// <summary>Stock levels and manual adjustments, for catalog staff.</summary>
[Route("api/v1/inventory/items")]
[Authorize(Policy = AccessPolicies.Collaborator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class InventoryItemsController(
    OpenInventoryItemHandler open,
    GetInventoryItemHandler get,
    AdjustStockHandler adjust
) : ApiControllerBase
{
    /// <summary>Starts tracking the stock of a SKU (BR-INV-008). The item starts with no units on hand.</summary>
    /// <param name="request">The SKU to track.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Open(
        [FromBody] OpenInventoryItemRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await open.HandleAsync(new OpenInventoryItemCommand(request.Sku), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Created(
            new Uri($"/api/v1/inventory/items/{result.Value.Sku}", UriKind.Relative),
            ToResponse(result.Value)
        );
    }

    /// <summary>Gets the stock level of a SKU. Returns an <c>ETag</c> derived from the resource version.</summary>
    /// <param name="sku">Stock-keeping unit (case-insensitive).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{sku}")]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(
        [RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)]
            string sku,
        CancellationToken cancellationToken
    )
    {
        var result = await get.HandleAsync(new GetInventoryItemQuery(sku), cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// Records a manual stock movement. The level never goes below the reserved quantity; the movement is
    /// appended to the stock ledger and never edited.
    /// </summary>
    /// <param name="sku">Stock-keeping unit (case-insensitive).</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result and never adjusts twice.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Signed quantity and reason.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{sku}/adjustments")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Adjust(
        [RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)]
            string sku,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken
    )
    {
        if (CurrentUserId is not { } actor)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new AdjustStockCommand(
            actor,
            sku,
            request.Delta,
            request.ReasonCode,
            idempotencyKey,
            ETags.TryParseVersion(ifMatch, out var version) ? version : null
        );
        var result = await adjust.HandleAsync(command, cancellationToken);

        return Respond(result);
    }

    private IActionResult Respond(Result<InventoryItemView> result)
    {
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Ok(ToResponse(result.Value));
    }

    private static InventoryItemResponse ToResponse(InventoryItemView view) =>
        new(view.Sku, view.OnHand, view.Reserved, view.Available, view.Version);
}
