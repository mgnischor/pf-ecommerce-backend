using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Attributes every request to a bounded context and an actor (ai/OBSERVABILITY.md §3.2, §3.3): the server span gets
/// <c>app.bounded_context</c> (from the controller's namespace, so the filter knows no context), the opaque account id as
/// <c>app.actor.id</c>, and <c>app.outcome=rejected</c> when the use case refused with a 4xx (an expected business
/// outcome, never an error status). A logging scope carries the same two values, so every log record written while the
/// action runs has them. Library instrumentation owns the span itself; this only enriches it. Never the e-mail or a name.
/// </summary>
internal sealed partial class ActionTelemetryFilter(ILogger<ActionTelemetryFilter> logger) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var boundedContext = BoundedContextOf(context.Controller.GetType().Namespace);
        var actor = context.HttpContext.User.GetUserId();
        var span = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;

        if (boundedContext is not null)
        {
            span?.SetTag(TelemetryNames.BoundedContext, boundedContext);
        }

        if (actor is { } id)
        {
            span?.SetTag(TelemetryNames.ActorId, id.ToString("D"));
            span?.SetTag(TelemetryNames.ActorType, "user");
        }

        using var scope = logger.BeginScope(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TelemetryNames.BoundedContext] = boundedContext,
                [TelemetryNames.ActorId] = actor?.ToString("D"),
            }
        );

        var executed = await next();

        if (
            executed.Result
            is ObjectResult { StatusCode: >= 400 and < 500 }
                or StatusCodeResult { StatusCode: >= 400 and < 500 }
        )
        {
            span?.SetTag(TelemetryNames.Outcome, "rejected");
        }
    }

    private static string? BoundedContextOf(string? controllerNamespace)
    {
        var match = ControllerNamespace().Match(controllerNamespace ?? string.Empty);
        return match.Success ? match.Groups["context"].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(
        @"^Portfolio\.(?<context>\w+)\.API\.Controllers$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ControllerNamespace();
}
