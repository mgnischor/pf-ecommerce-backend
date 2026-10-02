using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Portfolio.IntegrationTests.Caching;

/// <summary>Collects every log record (rendered message, level, scope values) so a test can assert on what was written.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _records = new();

    public IReadOnlyCollection<string> Records => [.. _records];

    public ILogger CreateLogger(string categoryName) => new Capture(_records, categoryName);

    public void Dispose()
    {
        // Nothing to release: the records are plain strings.
    }

    private sealed class Capture(ConcurrentQueue<string> records, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var attributes = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            records.Enqueue($"{logLevel} {category} [{eventId.Name}] {formatter(state, exception)} {attributes}");
        }
    }
}
