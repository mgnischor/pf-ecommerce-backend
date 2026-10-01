using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Base class of every controller. Controllers are <c>internal</c> like the rest of the codebase; they are
/// discovered by <see cref="InternalControllerFeatureProvider"/>. Declares the error responses shared by all operations.
/// </summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
internal abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Stable error code of <see cref="NotImplementedYet"/>.</summary>
    public const string NotImplementedCode = "ENDPOINT_NOT_IMPLEMENTED";

    /// <summary>The validated principal's account identifier, or <c>null</c> when anonymous.</summary>
    protected Guid? CurrentUserId => User.GetUserId();

    /// <summary>The validated principal's access level (<see cref="AccessLevel.Public"/> when anonymous).</summary>
    protected AccessLevel CurrentAccessLevel => User.GetAccessLevel();

    /// <summary>The validated principal, for claims the base class does not expose.</summary>
    protected ClaimsPrincipal Principal => User;

    /// <summary>
    /// Answers an operation that is mapped and documented but whose use case is not implemented yet,
    /// with RFC 9457 Problem Details. Replace the call with the real handler invocation.
    /// </summary>
    protected ObjectResult NotImplementedYet()
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            StatusCodes.Status501NotImplemented,
            title: "This operation is not available yet.",
            type: "https://errors.example.com/endpoint-not-implemented",
            detail: "The endpoint is part of the API contract but its use case has not been implemented.",
            instance: Request.Path
        );
        problem.Extensions["code"] = NotImplementedCode;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status501NotImplemented };
    }

    /// <summary>Maps a failed use case to its RFC 9457 response through the central mapping.</summary>
    /// <param name="error">The failure reported by the Application layer.</param>
    protected ObjectResult ProblemFrom(Error error) =>
        ErrorProblemDetails.Create(ProblemDetailsFactory, HttpContext, error);
}
