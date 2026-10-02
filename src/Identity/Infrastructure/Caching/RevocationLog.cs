namespace Portfolio.Identity.Infrastructure;

/// <summary>Log messages of the revocation blocklist. Exception types only: never token identifiers.</summary>
internal static partial class RevocationLog
{
    [LoggerMessage(
        EventId = 1010,
        EventName = "identity.revocation_check.failed",
        Level = LogLevel.Error,
        Message = "The token revocation blocklist could not be read ({ErrorType}); the check {Decision} the token."
    )]
    public static partial void CheckFailed(ILogger logger, string errorType, string decision);
}
