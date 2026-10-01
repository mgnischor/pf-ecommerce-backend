using Microsoft.AspNetCore.Mvc.Infrastructure;
using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Answers <c>409 Conflict</c> (RFC 9457) when a commit loses a race that the use case's own checks cannot close:
/// an optimistic-concurrency mismatch or a unique key taken by a concurrent request (ai/DATABASE.md §7.1,
/// ai/BUSINESS.md §5.3). The client re-reads the resource and retries; the body names the failure with a stable
/// code and carries nothing from the failed statement. Everything else stays with the fail-closed exception handler.
/// </summary>
internal sealed class PersistenceConflictMiddleware(RequestDelegate next)
{
    private const string TypeBase = "https://errors.example.com/";

    /// <summary>Runs the rest of the pipeline and translates a persistence conflict.</summary>
    /// <param name="context">Current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (PersistenceConflictException exception) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, exception);
        }
    }

    private static async Task WriteAsync(HttpContext context, PersistenceConflictException exception)
    {
        var factory = context.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(
            context,
            StatusCodes.Status409Conflict,
            title: "The request conflicts with the current state of the resource.",
            type: TypeBase + "conflict",
            instance: context.Request.Path
        );
        problem.Extensions["code"] = exception.Code;
        problem.Extensions["errors"] = new[] { new { code = exception.Code } };

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted
        );
    }
}
