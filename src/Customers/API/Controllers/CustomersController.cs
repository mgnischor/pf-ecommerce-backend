using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Customers.API.Contracts;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;

namespace Portfolio.Customers.API.Controllers;

/// <summary>Customer profiles. <c>me</c> always means the authenticated customer, so no identifier is exposed.</summary>
[Route("api/v1/customers")]
[Authorize(Policy = AccessPolicies.Authenticated)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class CustomersController : ApiControllerBase
{
    /// <summary>Gets the profile of the authenticated customer with masked contact data.</summary>
    [HttpGet("me")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult GetProfile() => NotImplementedYet();
}
