using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Portfolio.Cart;
using Portfolio.Catalog;
using Portfolio.Catalog.Infrastructure;
using Portfolio.Customers;
using Portfolio.Customers.Infrastructure;
using Portfolio.Identity;
using Portfolio.Identity.Infrastructure;
using Portfolio.Inventory;
using Portfolio.Inventory.Infrastructure;
using Portfolio.Ordering;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Health;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping;
using Portfolio.Shipping.Infrastructure;
using Scalar.AspNetCore;

// Compose healthcheck mode: the chiseled image has no shell or curl, so the app probes its own readiness
// endpoint and reports through the exit code (ai/CONTAINERS.md §13.2).
if (HealthProbe.IsProbeRequest(args))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"), TimeSpan.FromSeconds(3));
}

var builder = WebApplication.CreateBuilder(args);

AddConfigurationFolder(builder);
AddMountedSecrets(builder);

// Shutdown must finish inside the orchestrator's grace period (Compose stop_grace_period, Kubernetes
// terminationGracePeriodSeconds), which is deliberately larger (ai/CONTAINERS.md §8.2).
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = builder.Configuration.GetValue("Hosting:ShutdownTimeout", TimeSpan.FromSeconds(25))
);

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
builder.Services.AddApiHealthChecks();
builder.Services.AddObservability(builder.Logging, builder.Configuration, builder.Environment);
builder.Services.AddPostgres(builder.Configuration, builder.Environment);
builder.Services.AddValkey(builder.Configuration);
builder.Services.AddPageCursors(builder.Configuration, builder.Environment);
builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);
builder.Services.AddCatalogModule(builder.Environment);
builder.Services.AddCartModule(builder.Environment);
builder.Services.AddCustomersModule(builder.Environment);
builder.Services.AddInventoryModule(builder.Environment);
builder.Services.AddOrderingModule(builder.Environment);
builder.Services.AddShippingModule(builder.Environment);
AddMessaging(builder);
builder
    .Services.AddControllers(options =>
    {
        // Attributes every request to a bounded context and an actor, on the server span and in the log scope.
        options.Filters.Add<ActionTelemetryFilter>();
    })
    .ConfigureApplicationPartManager(manager =>
    {
        // Controllers are internal like the rest of the codebase; the default provider only finds public ones.
        manager.FeatureProviders.Remove(manager.FeatureProviders.OfType<ControllerFeatureProvider>().Single());
        manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
    })
    .AddJsonOptions(options =>
    {
        // One naming policy for the whole API: camelCase properties and camelCase string enums (ai/API_CONTRACTS.md §3).
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();

var app = builder.Build();

// First, so the identifier is on every response, including the ones the exception handler writes.
app.UseMiddleware<RequestCorrelationMiddleware>();

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
    app.MapOpenApi("/api/v1/openapi/v1.json").AllowAnonymous();
    app.MapScalarApiReference(
            "/api/v1/docs",
            options =>
            {
                options.WithOpenApiRoutePattern("/api/v1/openapi/v1.json");
                options.WithTheme(ScalarTheme.DeepSpace);
                options.WithTitle("E-Commerce API");
                options.WithYamlDocumentDownload();
            }
        )
        .AllowAnonymous();
}

app.UseMiddleware<PersistenceConflictMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapApiHealthChecks();

// Unmatched routes answer 404 (rendered as Problem Details by the status code pages) instead of being
// challenged by the default-deny fallback policy, which would hide the difference between "unknown" and "protected".
app.MapFallback("{*path}", () => Results.NotFound()).AllowAnonymous();
await app.RunAsync();
return 0;

// Publishing (the outbox relays) and consuming belong to the worker role (ai/CONTAINERS.md §6.1): each runs only where
// its setting is true, and only then does the process need the broker. Elsewhere events wait in the outbox.
static void AddMessaging(WebApplicationBuilder builder)
{
    var relays = builder.Configuration.GetValue<bool>($"{OutboxRelayOptions.SectionName}:Enabled");
    var consumers = builder.Configuration.GetValue<bool>($"{RabbitMqOptions.SectionName}:ConsumersEnabled");
    if (!relays && !consumers)
    {
        return;
    }

    builder.Services.AddRabbitMq(builder.Configuration);

    if (relays)
    {
        builder.Services.AddOutboxRelay<CatalogDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
        builder.Services.AddOutboxRelay<CustomersDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
        builder.Services.AddOutboxRelay<IdentityDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
        builder.Services.AddOutboxRelay<InventoryDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
        builder.Services.AddOutboxRelay<OrderingDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
        builder.Services.AddOutboxRelay<ShippingDbContext, RabbitMqOutboxPublisher>(builder.Configuration);
    }

    if (consumers)
    {
        builder.Services.AddRabbitMqConsumers();
    }
}

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

// Docker/Compose secrets are files under /run/secrets (ai/SECURITY.md §5.3): a file named
// Identity__TokenHashKey becomes the setting Identity:TokenHashKey. Only files whose name is a setting
// (it contains "__") are loaded; other secrets, such as key files, are read from their path where needed.
// They are added last, so they win over every other source, and are optional so local runs are unaffected.
static void AddMountedSecrets(WebApplicationBuilder builder)
{
    var directory = Environment.GetEnvironmentVariable("SECRETS_DIRECTORY") ?? "/run/secrets";
    if (!Directory.Exists(directory))
    {
        return;
    }

    builder.Configuration.AddKeyPerFile(source =>
    {
        source.FileProvider = new PhysicalFileProvider(directory);
        source.Optional = true;
        source.IgnoreCondition = fileName => !fileName.Contains("__", StringComparison.Ordinal);
    });
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
