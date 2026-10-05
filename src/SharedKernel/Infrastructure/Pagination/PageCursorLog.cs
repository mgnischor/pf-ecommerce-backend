namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of <see cref="HmacPageCursorCodec"/>.</summary>
internal static partial class PageCursorLog
{
    [LoggerMessage(
        EventId = 1600,
        Level = LogLevel.Warning,
        Message = "No Pagination:CursorKey configured: using an ephemeral development key. Cursors stop validating on restart."
    )]
    public static partial void EphemeralKey(ILogger logger);
}
