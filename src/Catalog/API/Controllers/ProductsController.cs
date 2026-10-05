using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Catalog.API.Contracts;
using Portfolio.Catalog.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.API.Controllers;

/// <summary>Product catalog: discovery for everyone, maintenance for catalog staff.</summary>
[Route("api/v1/products")]
internal sealed class ProductsController(
    ListProductsHandler list,
    GetProductHandler get,
    CreateProductHandler create,
    UpdateProductHandler update,
    ChangeProductPriceHandler changePrice,
    ActivateProductHandler activate,
    DiscontinueProductHandler discontinue,
    DeleteProductHandler delete
) : ApiControllerBase
{
    /// <summary>
    /// Lists products, newest first unless <paramref name="sort"/> says otherwise. Anonymous callers and customers
    /// only ever see <c>active</c> products; catalog staff see every status.
    /// </summary>
    /// <param name="limit">Page size, 1–100 (default 20).</param>
    /// <param name="cursor">Opaque cursor returned by the previous page; valid only with the same filters and sort.</param>
    /// <param name="q">Text search over the name (substring) and the SKU (exact), 2–100 characters.</param>
    /// <param name="sort">One of <c>name</c>, <c>price</c>, <c>createdAt</c>, optionally prefixed with <c>-</c>.</param>
    /// <param name="status">Filter by status. Callers who are not catalog staff only ever see <c>active</c> products.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<CursorPage<ProductSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List(
        [FromQuery, Range(1, 100)] int limit = ListProductsHandler.DefaultLimit,
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
        [FromQuery] ProductStatusContract? status = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = new ListProductsQuery(
            limit,
            cursor,
            q,
            sort,
            status is { } contract ? ToView(contract) : null,
            IsCatalogStaff
        );
        var result = await list.HandleAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        var page = result.Value;
        return Ok(
            new CursorPage<ProductSummaryResponse>([.. page.Items.Select(ToSummary)], page.NextCursor, page.HasMore)
        );
    }

    /// <summary>Gets a product. Returns an <c>ETag</c> derived from the resource version.</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid productId, CancellationToken cancellationToken)
    {
        var result = await get.HandleAsync(new GetProductQuery(productId, IsCatalogStaff), cancellationToken);
        return Respond(result);
    }

    /// <summary>Adds a product in <c>draft</c> status.</summary>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="request">Product data.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost]
    [Authorize(Policy = AccessPolicies.Collaborator)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new CreateProductCommand(
            request.Name,
            request.Sku,
            MoneyContract.AmountOf(request.Price),
            request.Price.Currency,
            request.Description,
            idempotencyKey
        );
        var result = await create.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Created(new Uri($"/api/v1/products/{result.Value.Id}", UriKind.Relative), ToResponse(result.Value));
    }

    /// <summary>Renames a product or changes its description (BR-CAT-001).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Fields to change.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPatch("{productId:guid}")]
    [Authorize(Policy = AccessPolicies.Collaborator)]
    [Consumes("application/merge-patch+json", "application/json")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Update(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new UpdateProductCommand(
            productId,
            request.NameSpecified,
            request.Name,
            request.DescriptionSpecified,
            request.Description,
            ExpectedVersion(ifMatch)
        );

        return Respond(await update.HandleAsync(command, cancellationToken));
    }

    /// <summary>Replaces the sell price (BR-CAT-002). Emits a price-changed event when the value differs.</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">New price.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPut("{productId:guid}/price")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ChangePrice(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] ChangeProductPriceRequest request,
        CancellationToken cancellationToken
    )
    {
        var command = new ChangeProductPriceCommand(
            productId,
            MoneyContract.AmountOf(request.Price),
            request.Price.Currency,
            ExpectedVersion(ifMatch)
        );

        return Respond(await changePrice.HandleAsync(command, cancellationToken));
    }

    /// <summary>Makes a draft product sellable (BR-CAT-003: Draft → Active).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{productId:guid}/activation")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public async Task<IActionResult> Activate(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        CancellationToken cancellationToken
    ) =>
        Respond(
            await activate.HandleAsync(
                new ActivateProductCommand(productId, ExpectedVersion(ifMatch)),
                cancellationToken
            )
        );

    /// <summary>Permanently withdraws an active product (BR-CAT-003: Active → Discontinued).</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("{productId:guid}/discontinuation")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public async Task<IActionResult> Discontinue(
        Guid productId,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        CancellationToken cancellationToken
    ) =>
        Respond(
            await discontinue.HandleAsync(
                new DiscontinueProductCommand(productId, ExpectedVersion(ifMatch)),
                cancellationToken
            )
        );

    /// <summary>Logically deletes a product (soft delete): its SKU is released and its history is kept.</summary>
    /// <param name="productId">Product identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result.</param>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpDelete("{productId:guid}")]
    [Authorize(Policy = AccessPolicies.Administrator)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    public async Task<IActionResult> Delete(
        Guid productId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        CancellationToken cancellationToken
    )
    {
        var command = new DeleteProductCommand(productId, idempotencyKey, ExpectedVersion(ifMatch));
        var result = await delete.HandleAsync(command, cancellationToken);

        return result.IsFailure ? ProblemFrom(result.Error) : NoContent();
    }

    // Everyone below the collaborator level, anonymous visitors included, sees the public catalog only.
    private bool IsCatalogStaff => CurrentAccessLevel >= AccessLevel.Collaborator;

    private static int? ExpectedVersion(string ifMatch) =>
        ETags.TryParseVersion(ifMatch, out var version) ? version : null;

    private IActionResult Respond(Result<ProductView> result)
    {
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Ok(ToResponse(result.Value));
    }

    private static ProductResponse ToResponse(ProductView view) =>
        new(
            view.Id,
            view.Name,
            view.Description,
            view.Sku,
            MoneyContract.ToResponse(view.PriceAmount, view.PriceCurrency),
            ToContract(view.Status),
            view.Version,
            view.AllowedActions,
            view.CreatedAt.UtcDateTime,
            view.UpdatedAt.UtcDateTime
        );

    private static ProductSummaryResponse ToSummary(ProductSummaryView view) =>
        new(
            view.Id,
            view.Name,
            view.Sku,
            MoneyContract.ToResponse(view.PriceAmount, view.PriceCurrency),
            ToContract(view.Status)
        );

    private static ProductStatusContract ToContract(ProductStatusView status) =>
        status switch
        {
            ProductStatusView.Draft => ProductStatusContract.Draft,
            ProductStatusView.Active => ProductStatusContract.Active,
            ProductStatusView.Discontinued => ProductStatusContract.Discontinued,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown product status."),
        };

    private static ProductStatusView ToView(ProductStatusContract status) =>
        status switch
        {
            ProductStatusContract.Draft => ProductStatusView.Draft,
            ProductStatusContract.Active => ProductStatusView.Active,
            ProductStatusContract.Discontinued => ProductStatusView.Discontinued,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown product status."),
        };
}
