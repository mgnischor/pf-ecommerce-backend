using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.SharedKernel.API.Controllers;

/// <summary>Technical diagnostics, reserved for the developer access level (the highest).</summary>
[Route("api/v1/diagnostics")]
[Authorize(Policy = AccessPolicies.Developer)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
internal sealed class DiagnosticsController(IHostEnvironment environment, TimeProvider timeProvider) : ApiControllerBase
{
    /// <summary>Returns build and runtime facts. Never includes configuration, secrets, or personal data.</summary>
    [HttpGet("runtime")]
    [ProducesResponseType<RuntimeDiagnosticsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetRuntime()
    {
        var now = timeProvider.GetUtcNow();
        using var process = Process.GetCurrentProcess();
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
        var version =
            typeof(DiagnosticsController)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "unknown";

        return Ok(
            new RuntimeDiagnosticsResponse(
                version,
                environment.EnvironmentName,
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount,
                now,
                (long)(now - started).TotalSeconds
            )
        );
    }
}
