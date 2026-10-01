using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Inventory.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Inventory.API.Controllers;

/// <summary>Stock levels and manual adjustments, for catalog staff.</summary>
[Route("api/v1/inventory/items")]
[Authorize(Policy = AccessPolicies.Collaborator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class InventoryItemsController : ApiControllerBase
{
    /// <summary>Gets the stock level of a SKU.</summary>
    /// <param name="sku">Stock-keeping unit (case-insensitive).</param>
    [HttpGet("{sku}")]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(
        [RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)]
            string sku
    ) => NotImplementedYet();

    /// <summary>
    /// Records a manual stock movement. The level never goes below the reserved quantity; the movement is
    /// appended to the stock ledger and never edited.
    /// </summary>
    /// <param name="sku">Stock-keeping unit (case-insensitive).</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result and never adjusts twice.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Signed quantity and reason.</param>
    [HttpPost("{sku}/adjustments")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<InventoryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult Adjust(
        [RegularExpression(ContractPatterns.Sku, MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds)]
            string sku,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] AdjustStockRequest request
    ) => NotImplementedYet();
}
