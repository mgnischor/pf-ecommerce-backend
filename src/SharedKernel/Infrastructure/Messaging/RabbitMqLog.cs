namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of the RabbitMQ adapter. Exception types only: never the connection string, addresses or payloads.</summary>
internal static partial class RabbitMqLog
{
    [LoggerMessage(
        EventId = 1400,
        EventName = "app.messaging.connected",
        Level = LogLevel.Information,
        Message = "Connected to RabbitMQ and declared the exchanges."
    )]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(
        EventId = 1401,
        EventName = "app.messaging.connect_failed",
        Level = LogLevel.Warning,
        Message = "Could not connect to RabbitMQ: {ErrorType}. Events stay in the outbox."
    )]
    public static partial void ConnectFailed(ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 1402,
        EventName = "app.messaging.consuming",
        Level = LogLevel.Information,
        Message = "Consuming {Consumers} queue(s) from RabbitMQ."
    )]
    public static partial void Consuming(ILogger logger, int consumers);

    [LoggerMessage(
        EventId = 1403,
        EventName = "app.messaging.subscribe_failed",
        Level = LogLevel.Warning,
        Message = "Could not start the RabbitMQ consumers: {ErrorType}. Retrying."
    )]
    public static partial void SubscribeFailed(ILogger logger, string errorType);

    [LoggerMessage(
        EventId = 1404,
        EventName = "app.messaging.handler_failed",
        Level = LogLevel.Warning,
        Message = "Consumer {Consumer} failed on delivery {Attempt}: {ErrorType}. The message is requeued."
    )]
    public static partial void HandlerFailed(ILogger logger, string consumer, int attempt, string errorType);

    [LoggerMessage(
        EventId = 1405,
        EventName = "app.messaging.poisoned",
        Level = LogLevel.Error,
        Message = "Consumer {Consumer} dead-lettered a message that can never be handled: {ErrorType}."
    )]
    public static partial void Poisoned(ILogger logger, string consumer, string errorType);

    [LoggerMessage(
        EventId = 1406,
        EventName = "app.messaging.settle_failed",
        Level = LogLevel.Warning,
        Message = "Consumer {Consumer} could not settle a message: {ErrorType}. It will be redelivered."
    )]
    public static partial void SettleFailed(ILogger logger, string consumer, string errorType);
}
