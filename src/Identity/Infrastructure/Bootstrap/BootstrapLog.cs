namespace Portfolio.Identity.Infrastructure;

/// <summary>Log messages of <see cref="IdentityBootstrapper"/>. Counts only: never e-mails or passwords.</summary>
internal static partial class BootstrapLog
{
    [LoggerMessage(
        EventId = 1120,
        Level = LogLevel.Information,
        Message = "Identity bootstrap finished: {Created} of {Configured} configured accounts created."
    )]
    public static partial void Completed(ILogger logger, int created, int configured);
}
