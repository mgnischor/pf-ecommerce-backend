namespace Portfolio.Identity.Infrastructure;

/// <summary>Log messages of <see cref="RefreshTokenCodec"/>.</summary>
internal static partial class RefreshTokenCodecLog
{
    [LoggerMessage(
        EventId = 1110,
        Level = LogLevel.Warning,
        Message = "No Identity:TokenHashKey configured: using an ephemeral development key. Refresh tokens stop validating on restart."
    )]
    public static partial void EphemeralKey(ILogger logger);
}
