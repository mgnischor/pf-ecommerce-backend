using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Portfolio.IntegrationTests.Caching;

namespace Portfolio.IntegrationTests.Telemetry;

/// <summary>
/// Captures what the application exports, in memory (ai/OBSERVABILITY.md §17: tests use in-memory exporters, never a real
/// Collector): finished spans, collected metrics, and every log record. Attach it to a host, drive the API, assert.
/// </summary>
internal sealed class TelemetryCapture
{
    private readonly Lock _gate = new();
    private readonly List<Activity> _spans = [];

    public List<Metric> Metrics { get; } = [];

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>A copy of the spans finished so far.</summary>
    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_gate)
            {
                return [.. _spans];
            }
        }
    }

    public void Attach(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            services.ConfigureOpenTelemetryTracerProvider(
                (_, tracing) => tracing.AddProcessor(new CopyingProcessor(this))
            );
            services.ConfigureOpenTelemetryMeterProvider((_, metrics) => metrics.AddInMemoryExporter(Metrics));
        });
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    /// <summary>Waits until a span matching <paramref name="predicate"/> has finished (a server span ends just after the response).</summary>
    public async Task<Activity> WaitForSpanAsync(Func<Activity, bool> predicate, string description)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var found = Spans.FirstOrDefault(predicate);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No span matched: {description}");
    }

    /// <summary>Every attribute value of every span, as text: tags and the attributes of their events.</summary>
    public IEnumerable<string> AllSpanText() =>
        Spans.SelectMany(span =>
            span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")
                .Concat(
                    span.Events.SelectMany(@event => @event.Tags.Select(tag => $"{@event.Name}.{tag.Key}={tag.Value}"))
                )
                .Append(span.DisplayName)
        );

    private void Add(Activity activity)
    {
        lock (_gate)
        {
            _spans.Add(activity);
        }
    }

    // A simple processor keeps the finished Activity; the in-memory exporter would do the same, without the lock.
    private sealed class CopyingProcessor(TelemetryCapture owner) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => owner.Add(data);
    }
}
