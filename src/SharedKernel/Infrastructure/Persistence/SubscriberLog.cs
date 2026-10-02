namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of the post-commit event dispatch. Type names only: never event payloads.</summary>
internal static partial class SubscriberLog
{
    [LoggerMessage(
        EventId = 1220,
        EventName = "app.domain_event.subscriber_failed",
        Level = LogLevel.Warning,
        Message = "Subscriber {Subscriber} failed on {EventType}: {ErrorType}. The change is committed and the event is in the outbox."
    )]
    public static partial void Failed(ILogger logger, string subscriber, string eventType, string errorType);
}
