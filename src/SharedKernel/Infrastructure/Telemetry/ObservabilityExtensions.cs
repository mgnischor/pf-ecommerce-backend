using System.Reflection;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Bootstrap of the OpenTelemetry SDK for traces, metrics and logs (ai/OBSERVABILITY.md §3.5, §16): composition root
/// only. Domain code has no telemetry. Library instrumentation covers ASP.NET Core, <c>HttpClient</c>, Npgsql and the
/// runtime; custom sources and meters are all named <c>Ecommerce.*</c> and registered here, because an unregistered one is
/// silently dropped. Everything is configured by the standard <c>OTEL_*</c> variables, so the same image runs in every
/// environment, and export is asynchronous and bounded: an unreachable Collector never slows or fails a request.
/// </summary>
internal static class ObservabilityExtensions
{
    /// <summary>The service name when <c>OTEL_SERVICE_NAME</c> is not set (the worker role sets its own).</summary>
    public const string DefaultServiceName = "ecommerce-api";

    /// <summary>The <c>service.namespace</c> resource attribute (maps to the IaC <c>project</c> tag).</summary>
    public const string ServiceNamespace = "ecommerce";

    /// <summary>
    /// Boundaries of <c>http.server.request.duration</c> in seconds. Every latency threshold used by an SLO or an alert
    /// must be a boundary (a 300 ms objective needs <c>0.3</c>), or the SLI cannot be computed from the histogram.
    /// </summary>
    public static readonly double[] HttpDurationBoundaries =
    [
        0.005,
        0.01,
        0.025,
        0.05,
        0.1,
        0.2,
        0.3,
        0.5,
        0.8,
        1,
        2,
        5,
        10,
    ];

    /// <summary>Registers the SDK, the instrumentation, the log provider and, when an endpoint is configured, the OTLP exporter.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="logging">Logging builder of the host.</param>
    /// <param name="configuration">Application configuration (includes the environment variables).</param>
    /// <param name="environment">Host environment: JSON console output everywhere but Development.</param>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        ILoggingBuilder logging,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? DefaultServiceName;
        var version =
            typeof(ObservabilityExtensions)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "0.0.0";

        var openTelemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource
                    // Defaults first, then the environment: OTEL_RESOURCE_ATTRIBUTES (deployment.environment.name,
                    // app.owner, service.version of the build…) overrides them. service.instance.id is generated.
                    .AddService(serviceName, ServiceNamespace, version, autoGenerateServiceInstanceId: true)
                    .AddEnvironmentVariableDetector()
            )
            .WithTracing(ConfigureTracing)
            .WithMetrics(ConfigureMetrics)
            .WithLogging(
                _ => { },
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                }
            );

        // Without an endpoint nothing is exported (a local run, the tests); with one, endpoint, protocol, headers and TLS
        // come from OTEL_EXPORTER_OTLP_*, and the SDK's bounded queues drop data instead of blocking callers.
        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        if (!environment.IsDevelopment())
        {
            // One JSON object per line, for `docker logs` and `kubectl logs`; the OTLP path is the canonical one.
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
            });
        }

        return services;
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing) =>
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                // Probes are noise (ai/OBSERVABILITY.md §4.4); the route template, not the URL, names the span.
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddSource(TelemetryNames.CacheSource, TelemetryNames.MessagingSource);

    private static void ConfigureMetrics(MeterProviderBuilder metrics) =>
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(TelemetryNames.MeterPattern, "Npgsql")
            .AddView(
                "http.server.request.duration",
                new ExplicitBucketHistogramConfiguration { Boundaries = HttpDurationBoundaries }
            );
}
