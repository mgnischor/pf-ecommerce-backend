using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Identity.API.Contracts;
using Portfolio.SharedKernel.API;

namespace Portfolio.Identity.API.Controllers;

/// <summary>Sign-in.</summary>
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>
    /// Exchanges credentials for tokens. Failures are uniform (no account enumeration) and the endpoint is
    /// rate limited per client and per account.
    /// </summary>
    /// <param name="request">Account credentials.</param>
    [HttpPost("tokens")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public IActionResult CreateToken([FromBody] CreateTokenRequest request) => NotImplementedYet();
}
