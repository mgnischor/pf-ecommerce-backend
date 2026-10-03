using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Customers.API.Contracts;
using Portfolio.Customers.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.API.Controllers;

/// <summary>Customer profiles. <c>me</c> always means the authenticated customer, so no identifier is exposed.</summary>
[Route("api/v1/customers")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class CustomersController(GetCustomerProfileHandler get, UpdateCustomerProfileHandler update)
    : ApiControllerBase
{
    /// <summary>
    /// Gets the profile of the authenticated customer with masked contact data (BR-CUS-009). Returns an <c>ETag</c>
    /// derived from the resource version. The profile is created asynchronously right after registration, so it can
    /// answer <c>404</c> for a moment; accounts of staff never have one.
    /// </summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("me")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not { } accountId)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        return Respond(await get.HandleAsync(new GetCustomerProfileQuery(accountId), cancellationToken));
    }

    /// <summary>
    /// Changes the profile of the authenticated customer (JSON Merge Patch): members left out keep their value, and
    /// <c>phone: null</c> removes the phone. Only the caller's own profile can be reached. The version moves only when
    /// something actually changed.
    /// </summary>
    /// <param name="ifMatch">ETag of the version the client read.</param>
    /// <param name="request">Members to change.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPatch("me")]
    [Consumes("application/merge-patch+json", "application/json")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> UpdateProfile(
        [FromHeader(Name = ApiHeaders.IfMatch), Required] string ifMatch,
        [FromBody] UpdateCustomerProfileRequest request,
        CancellationToken cancellationToken
    )
    {
        if (CurrentUserId is not { } accountId)
        {
            return ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));
        }

        var command = new UpdateCustomerProfileCommand(
            accountId,
            ETags.TryParseVersion(ifMatch, out var version) ? version : null,
            request.FullNameSet ? Change<string?>.To(request.FullName) : Change<string?>.Unchanged,
            request.PhoneSet ? Change<string?>.To(request.Phone) : Change<string?>.Unchanged,
            request.LocaleSet ? Change<string?>.To(request.Locale) : Change<string?>.Unchanged,
            request.TimeZoneSet ? Change<string?>.To(request.TimeZone) : Change<string?>.Unchanged
        );

        return Respond(await update.HandleAsync(command, cancellationToken));
    }

    private IActionResult Respond(Result<CustomerProfileView> result)
    {
        if (result.IsFailure)
        {
            return ProblemFrom(result.Error);
        }

        Response.Headers.ETag = ETags.ForVersion(result.Value.Version);
        return Ok(ToResponse(result.Value));
    }

    private static CustomerProfileResponse ToResponse(CustomerProfileView view) =>
        new(view.Id, view.FullName, view.MaskedEmail, view.MaskedPhone, view.Locale, view.TimeZone, view.Version);
}
