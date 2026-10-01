using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Catalog.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Catalog.API.Controllers;

/// <summary>Product catalog: discovery for everyone, maintenance for catalog staff.</summary>
[Route("api/v1/products")]
internal sealed class ProductsController : ApiControllerBase
{
    /// <summary>Lists products, newest first unless <paramref name="sort"/> says otherwise.</summary>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page.</param>
    /// <param name="q">Text search over name and SKU, 2–100 characters.</param>
    /// <param name="sort">One of <c>name</c>, <c>price</c>, <c>createdAt</c>, optionally prefixed with <c>-</c>.</param>
    /// <param name="status">Filter by status. Anonymous callers only ever see <c>active</c> products.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<CursorPage<ProductSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public IActionResult List(
        [FromQuery, Range(1, 100)] int limit = 20,
        [FromQuery, StringLength(512)] string? cursor = null,
        [FromQuery, StringLength(100, MinimumLength = 2)] string? q = null,
        [
            FromQuery,
            RegularExpression(
                ContractPatterns.SortExpression,
                MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
            )
        ]
            string? sort = null,
        [FromQuery] ProductStatusContract? status = null
    ) => NotImplementedYet();

    /// <summary>Gets a product. Returns an <c>ETag</c> derived from the resource version.</summary>
    /// <param name="productId">Product identifier.</param>
    [HttpGet("{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(Guid productId) => NotImplementedYet();

    /// <summary>Adds a product in <c>draft</c> status.</summary>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="request">Product data.</param>
    [HttpPost]
    [Authorize(Policy = AccessPolicies.Collaborator)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult Create(
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromBody] CreateProductRequest request
    ) => NotImplementedYet();

    /// <summary>Renames a product or changes its description (BR-CAT-001).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Fields to change.</param>
    [HttpPatch("{productId:guid}")]
    [Authorize(Policy = AccessPolicies.Collaborator)]
    [Consumes("application/merge-patch+json", "application/json")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult Update(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] UpdateProductRequest request
    ) => NotImplementedYet();

    /// <summary>Replaces the sell price (BR-CAT-002). Emits a price-changed event when the value differs.</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">New price.</param>
    [HttpPut("{productId:guid}/price")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult ChangePrice(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] ChangeProductPriceRequest request
    ) => NotImplementedYet();

    /// <summary>Makes a draft product sellable (BR-CAT-003: Draft → Active).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    [HttpPost("{productId:guid}/activation")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public IActionResult Activate(Guid productId, [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch) =>
        NotImplementedYet();

    /// <summary>Permanently withdraws an active product (BR-CAT-003: Active → Discontinued).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    [HttpPost("{productId:guid}/discontinuation")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public IActionResult Discontinue(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch
    ) => NotImplementedYet();

    /// <summary>Logically deletes a product (soft delete): its SKU is released and its history is kept.</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    [HttpDelete("{productId:guid}")]
    [Authorize(Policy = AccessPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public IActionResult Delete(
        Guid productId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch
    ) => NotImplementedYet();
}
