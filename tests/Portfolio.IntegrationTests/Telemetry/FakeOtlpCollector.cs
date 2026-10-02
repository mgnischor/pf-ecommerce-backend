using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Portfolio.IntegrationTests.Telemetry;

/// <summary>
/// A stand-in for the OpenTelemetry Collector on loopback: accepts OTLP/HTTP posts and keeps the raw protobuf bodies by
/// signal, so a test can prove that what the application exports really leaves the process, and what it must never contain.
/// </summary>
internal sealed class FakeOtlpCollector : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> _bodies;

    private FakeOtlpCollector(
        WebApplication app,
        ConcurrentDictionary<string, ConcurrentQueue<byte[]>> bodies,
        string endpoint
    )
    {
        _app = app;
        _bodies = bodies;
        Endpoint = endpoint;
    }

    /// <summary>Base URL the application is told to export to (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>).</summary>
    public string Endpoint { get; }

    public static async Task<FakeOtlpCollector> StartAsync()
    {
        var bodies = new ConcurrentDictionary<string, ConcurrentQueue<byte[]>>(StringComparer.Ordinal);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapPost(
            "/v1/{signal}",
            async (string signal, HttpContext context) =>
            {
                using var buffer = new MemoryStream();
                await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
                bodies.GetOrAdd(signal, _ => new ConcurrentQueue<byte[]>()).Enqueue(buffer.ToArray());
                return Results.Ok();
            }
        );
        await app.StartAsync();

        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        return new FakeOtlpCollector(app, bodies, address);
    }

    /// <summary>The raw bodies received for a signal (<c>traces</c>, <c>metrics</c>, <c>logs</c>), as one text for searching.</summary>
    public string Received(string signal) =>
        _bodies.TryGetValue(signal, out var queue)
            ? string.Join('\n', queue.Select(body => Encoding.Latin1.GetString(body)))
            : string.Empty;

    /// <summary>Waits until a signal satisfies a condition (the SDK exports in batches on a timer).</summary>
    public async Task<bool> WaitForAsync(string signal, Func<string, bool> condition, TimeSpan timeout)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < timeout)
        {
            if (condition(Received(signal)))
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
