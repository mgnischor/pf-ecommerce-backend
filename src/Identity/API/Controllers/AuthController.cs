using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Identity.API.Contracts;
using Portfolio.Identity.Application;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.RateLimiting;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.API.Controllers;

/// <summary>Sign-in, token lifecycle, registration, and the caller's own identity.</summary>
[Route("api/v1/auth")]
internal sealed class AuthController(
    SignInHandler signIn,
    RefreshTokenHandler refresh,
    RevokeTokenHandler revoke,
    RegisterCustomerHandler register
) : ApiControllerBase
{
    private const string BearerType = "Bearer";

    /// <summary>
    /// Exchanges credentials for tokens. Every failure is the same <c>401</c> with the same code, so the
    /// response never reveals whether an account exists; the account locks for 15 minutes after 5 failures.
    /// Rate limited per client.
    /// </summary>
    /// <param name="request">Account credentials.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("tokens")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimiting.AuthPolicy)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> CreateToken(
        [FromBody] CreateTokenRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await signIn.HandleAsync(new SignInCommand(request.Email, request.Password), cancellationToken);
        return result.IsFailure ? ProblemFrom(result.Error) : Ok(ToResponse(result.Value));
    }

    /// <summary>
    /// Rotates a refresh token: the presented token is consumed and a new pair is issued. Presenting a token
    /// that was already used revokes the whole session. Rate limited per client.
    /// </summary>
    /// <param name="request">The current refresh token.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("tokens/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimiting.AuthPolicy)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await refresh.HandleAsync(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
        return result.IsFailure ? ProblemFrom(result.Error) : Ok(ToResponse(result.Value));
    }

    /// <summary>
    /// Signs out: revokes the session behind the refresh token and blocks the caller's current access token
    /// until it would have expired. Always answers <c>204</c>, so it cannot be used to probe tokens.
    /// </summary>
    /// <param name="request">The session's refresh token.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("tokens/revocation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Revoke([FromBody] RevokeTokenRequest request, CancellationToken cancellationToken)
    {
        var accessTokenId = Principal.GetTokenId();
        DateTimeOffset? expiresAt = long.TryParse(
            Principal.FindFirst("exp")?.Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var exp
        )
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : null;

        var command = new RevokeTokenCommand(request.RefreshToken, accessTokenId, expiresAt, CurrentUserId);
        await revoke.HandleAsync(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Creates a customer account with the <c>public</c> access level. The request cannot name a level, so it
    /// can never be used to gain privileges. Rate limited per client.
    /// </summary>
    /// <param name="request">Account data.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpPost("registrations")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimiting.AuthPolicy)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await register.HandleAsync(
            new RegisterCustomerCommand(
                request.Email,
                request.Password,
                request.FullName,
                request.Phone,
                request.Locale,
                request.TimeZone
            ),
            cancellationToken
        );
        return result.IsFailure
            ? ProblemFrom(result.Error)
            : StatusCode(StatusCodes.Status201Created, new UserResponse(result.Value, AccessLevel.Public.ToWireName()));
    }

    /// <summary>Returns the caller's account identifier and access level, from the validated token.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AccessPolicies.Authenticated)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public IActionResult Me() =>
        CurrentUserId is { } id
            ? Ok(new CurrentUserResponse(id, CurrentAccessLevel.ToWireName()))
            : ProblemFrom(Error.Unauthorized("INVALID_TOKEN"));

    private static TokenResponse ToResponse(TokenPair pair) =>
        new(pair.AccessToken, BearerType, pair.ExpiresInSeconds, pair.RefreshToken);
}
