namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of the telemetry plumbing. Type names only: never data from the failed query.</summary>
internal static partial class TelemetryLog
{
    [LoggerMessage(
        EventId = 1230,
        EventName = "app.outbox.sample_failed",
        Level = LogLevel.Warning,
        Message = "The outbox backlog of {Context} could not be sampled: {ErrorType}. The gauges keep their last value."
    )]
    public static partial void OutboxSampleFailed(ILogger logger, string context, string errorType);
}
