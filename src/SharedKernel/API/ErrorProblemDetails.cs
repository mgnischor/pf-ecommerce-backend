using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// The single mapping from a domain <see cref="Error"/> to an RFC 9457 response (ai/API_CONTRACTS.md §4):
/// controllers never build error bodies or catch exceptions themselves. User-facing text is resolved from
/// <c>code</c> and <c>params</c> by clients; the server sends no prose beyond the constant <c>title</c>.
/// </summary>
internal static class ErrorProblemDetails
{
    private const string TypeBase = "https://errors.example.com/";

    /// <summary>Builds the response for <paramref name="error"/>.</summary>
    /// <param name="factory">Factory that adds <c>traceId</c> and the standard members.</param>
    /// <param name="context">Current request.</param>
    /// <param name="error">Domain failure.</param>
    public static ObjectResult Create(ProblemDetailsFactory factory, HttpContext context, Error error)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(error);

        var (status, slug, title) = Describe(error.Type);
        var problem = factory.CreateProblemDetails(
            context,
            status,
            title: title,
            type: TypeBase + slug,
            instance: context.Request.Path
        );

        problem.Extensions["code"] = error.Code;
        problem.Extensions["errors"] = new[]
        {
            new
            {
                ruleId = error.RuleId,
                field = error.Field is null ? null : "/" + error.Field,
                code = error.Code,
                @params = error.Parameters,
            },
        };

        if (error.Type == ErrorType.Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return new ObjectResult(problem) { StatusCode = status };
    }

    private static (int Status, string Slug, string Title) Describe(ErrorType type) =>
        type switch
        {
            ErrorType.Validation => (
                StatusCodes.Status422UnprocessableEntity,
                "business-rule-violation",
                "The request violates a business rule."
            ),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "not-found", "The resource was not found."),
            ErrorType.Conflict => (
                StatusCodes.Status409Conflict,
                "conflict",
                "The request conflicts with the current state of the resource."
            ),
            ErrorType.Unauthorized => (
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "Authentication is required or failed."
            ),
            ErrorType.Forbidden => (
                StatusCodes.Status403Forbidden,
                "forbidden",
                "You are not allowed to perform this action."
            ),
            ErrorType.PreconditionFailed => (
                StatusCodes.Status412PreconditionFailed,
                "precondition-failed",
                "The resource changed since it was read."
            ),
            _ => (StatusCodes.Status500InternalServerError, "internal-error", "An unexpected error occurred."),
        };
}
