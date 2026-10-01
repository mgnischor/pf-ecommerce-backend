namespace Portfolio.Identity.Infrastructure;

/// <summary>Log messages of <see cref="SigningKeyRing"/>.</summary>
internal static partial class SigningKeyLog
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "No JWT signing key configured: using an ephemeral development key. Tokens stop validating on restart."
    )]
    public static partial void EphemeralKey(ILogger logger);
}
