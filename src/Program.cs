using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration.Json;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

AddConfigurationFolder(builder);

// Hardened Kestrel defaults (ai/SECURITY.md §6.1–§6.2). Raise limits per endpoint, never globally.
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    options.Limits.MaxRequestLineSize = 8 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
    options.Limits.MinRequestBodyDataRate = new MinDataRate(240, TimeSpan.FromSeconds(5));
});

// The only place allowed to touch the system clock: everything else receives TimeProvider by injection.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Fail closed: unhandled exceptions become RFC 9457 responses without internals.
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api/v1/docs", StringComparison.OrdinalIgnoreCase),
    branch => branch.Use(AddSecurityHeaders)
);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/v1/openapi/v1.json");
    app.MapScalarApiReference(
        "/api/v1/docs",
        options =>
        {
            options.WithOpenApiRoutePattern("/api/v1/openapi/v1.json");
            options.WithTheme(ScalarTheme.DeepSpace);
            options.WithTitle("E-Commerce API");
            options.WithYamlDocumentDownload();
        }
    );
}

app.UseAuthorization();
app.MapControllers();
await app.RunAsync();

// Files live in configuration/ (copied next to the binaries), which the default host does not probe.
// They are inserted right after the default appsettings sources so user secrets, environment variables
// and the command line keep precedence.
static void AddConfigurationFolder(WebApplicationBuilder builder)
{
    var directory = Path.Combine(AppContext.BaseDirectory, "configuration");
    if (!Directory.Exists(directory))
    {
        return;
    }

    var sources = builder.Configuration.Sources;
    var insertAt = sources.ToList().FindLastIndex(source => source is JsonConfigurationSource) + 1;

    foreach (var fileName in new[] { "appsettings.json", $"appsettings.{builder.Environment.EnvironmentName}.json" })
    {
        var source = new JsonConfigurationSource { Path = Path.Combine(directory, fileName), Optional = true };
        source.ResolveFileProvider();
        sources.Insert(insertAt++, source);
    }
}

// Set in OnStarting because the exception handler clears headers written earlier in the pipeline.
static Task AddSecurityHeaders(HttpContext context, RequestDelegate next)
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";
        return Task.CompletedTask;
    });

    return next(context);
}

/// <summary>Entry point marker so integration tests can host the application with <c>WebApplicationFactory</c>.</summary>
[SuppressMessage(
    "Major Code Smell",
    "S1118:Utility classes should not have public constructors",
    Justification = "Marker required by WebApplicationFactory<Program> (ai/TESTS.md §2.3); it is not a utility class."
)]
public partial class Program;
