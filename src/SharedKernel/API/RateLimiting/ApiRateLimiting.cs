using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Portfolio.SharedKernel.API.RateLimiting;

/// <summary>
/// Rate limiting for the sensitive business flows (OWASP API4/API6, ai/SECURITY.md §6.1): sign-in, token
/// refresh, and registration. Partitioned per client address; rejected requests get <c>429</c> with
/// <c>Retry-After</c> and RFC 9457 Problem Details.
/// </summary>
internal static class ApiRateLimiting
{
    /// <summary>Policy name for authentication endpoints.</summary>
    public const string AuthPolicy = "auth";

    private const string SectionName = "RateLimiting:Auth";
    private const int DefaultPermitLimit = 10;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    /// <summary>Registers the policies. Limits come from <c>RateLimiting:Auth</c> so tests and operators can tune them.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Limits are read when the limiter is first built (from the final, layered configuration), not while
        // services are being registered, so every configuration source that applies to the host is honored.
        services.AddRateLimiter(_ => { });
        services
            .AddOptions<RateLimiterOptions>()
            .Configure<IConfiguration>(
                (options, settings) =>
                {
                    var section = settings.GetSection(SectionName);
                    var permitLimit = section.GetValue("PermitLimit", DefaultPermitLimit);
                    var window = section.GetValue("Window", DefaultWindow);

                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    options.AddPolicy(
                        AuthPolicy,
                        context =>
                            RateLimitPartition.GetFixedWindowLimiter(
                                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                                _ => new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit = permitLimit,
                                    Window = window,
                                    QueueLimit = 0,
                                }
                            )
                    );
                    options.OnRejected = WriteRejectionAsync;
                }
            );

        return services;
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                CultureInfo.InvariantCulture
            );
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Type = "https://errors.example.com/rate-limited",
            Title = "Too many requests.",
            Instance = context.HttpContext.Request.Path,
        };
        problem.Extensions["code"] = "RATE_LIMITED";

        await response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken
        );
    }
}
