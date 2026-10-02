namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of the cache boundary. Names and exception types only: never keys, values or the connection string.</summary>
internal static partial class CacheLog
{
    [LoggerMessage(
        EventId = 1300,
        EventName = "app.cache.failed",
        Level = LogLevel.Warning,
        Message = "Cache {CacheName} failed during {Operation}: {ErrorType}. The source of truth answered instead."
    )]
    public static partial void OperationFailed(ILogger logger, string cacheName, string operation, string errorType);
}
