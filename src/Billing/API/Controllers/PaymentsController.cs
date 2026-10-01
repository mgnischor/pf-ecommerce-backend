using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Billing.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;

namespace Portfolio.Billing.API.Controllers;

/// <summary>Payments, refunds, and payment-provider callbacks.</summary>
[Route("api/v1/payments")]
internal sealed class PaymentsController : ApiControllerBase
{
    /// <summary>Gets a payment with masked payment-method data. A payment of another customer answers <c>404</c>.</summary>
    /// <param name="paymentId">Payment identifier.</param>
    [HttpGet("{paymentId:guid}")]
    [Authorize(Policy = AccessPolicies.Authenticated)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Get(Guid paymentId) => NotImplementedYet();

    /// <summary>
    /// Refunds part or all of a captured payment. High-value refunds need a second approver and step-up
    /// authentication (<c>403</c> with <c>insufficient_user_authentication</c>).
    /// </summary>
    /// <param name="paymentId">Payment identifier.</param>
    /// <param name="idempotencyKey">Replaying the same key returns the original result and never refunds twice.</param>
    /// <param name="ifMatch">ETag of the payment version the client read.</param>
    /// <param name="request">Amount and reason.</param>
    [HttpPost("{paymentId:guid}/refunds")]
    [Authorize(Policy = AccessPolicies.Manager)]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult Refund(
        Guid paymentId,
        [
            FromHeader(Name = ApiHeaders.IdempotencyKey),
            Required,
            StringLength(ApiHeaders.IdempotencyKeyMaxLength, MinimumLength = ApiHeaders.IdempotencyKeyMinLength)
        ]
            string idempotencyKey,
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] RefundPaymentRequest request
    ) => NotImplementedYet();

    /// <summary>
    /// Receives an event from a payment provider. Anonymous by design: authenticity comes from the provider
    /// signature, which is verified and deduplicated by event ID before any state changes.
    /// </summary>
    /// <param name="provider">Provider key, for example <c>stripe</c>.</param>
    /// <param name="signature">Provider signature over the raw request body.</param>
    /// <param name="payload">Provider event; translated by an anti-corruption layer, never used directly.</param>
    [HttpPost("webhooks/{provider:regex(^[[a-z0-9-]]{{2,32}}$)}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public IActionResult ReceiveWebhook(
        string provider,
        [FromHeader(Name = "X-Webhook-Signature"), Required, StringLength(512)] string signature,
        [FromBody] JsonElement payload
    ) => NotImplementedYet();
}
