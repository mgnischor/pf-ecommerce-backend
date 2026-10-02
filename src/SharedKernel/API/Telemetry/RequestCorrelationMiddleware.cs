using System.Diagnostics;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Puts the trace identifier of the request on every response as <c>X-Request-ID</c> (ai/OBSERVABILITY.md §6.5, §8), so a
/// support ticket can be traced to the exact request: the same 32-hex identifier is the trace ID in the traces and in
/// every log record. The value is generated here, never copied from the caller: an incoming <c>X-Request-ID</c> is
/// untrusted input and could be used to forge or poison correlation.
/// </summary>
internal sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
    /// <summary>Name of the response header.</summary>
    public const string HeaderName = "X-Request-ID";

    /// <summary>Runs the rest of the pipeline with the header set on the response.</summary>
    /// <param name="context">Current request.</param>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        // OnStarting, because the exception handler clears headers written earlier in the pipeline.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        return next(context);
    }
}
