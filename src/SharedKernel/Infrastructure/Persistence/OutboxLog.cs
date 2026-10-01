namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of <see cref="OutboxRelay{TContext}"/>. Counts and exception types only: never payloads.</summary>
internal static partial class OutboxLog
{
    [LoggerMessage(
        EventId = 1210,
        Level = LogLevel.Warning,
        Message = "Outbox relay of {Context} failed to publish a message (attempt {Attempts}): {ErrorType}."
    )]
    public static partial void PublishFailed(ILogger logger, string context, int attempts, string errorType);

    [LoggerMessage(
        EventId = 1211,
        Level = LogLevel.Error,
        Message = "Outbox relay of {Context} could not process a batch: {ErrorType}."
    )]
    public static partial void BatchFailed(ILogger logger, string context, string errorType);
}
