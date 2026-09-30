using Microsoft.AspNetCore.Mvc;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Base class of every controller. Public because MVC only discovers public controllers; the rest of the
/// codebase stays <c>internal</c>. Declares the error responses shared by all operations.
/// </summary>
[ApiController]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status501NotImplemented, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Stable error code of <see cref="NotImplementedYet"/>.</summary>
    public const string NotImplementedCode = "ENDPOINT_NOT_IMPLEMENTED";

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
}
