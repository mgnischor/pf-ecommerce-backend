namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Log messages of <see cref="DevelopmentDatabaseMigrator"/>.</summary>
internal static partial class MigratorLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Database migrations applied for {Context} (development start-up migration)."
    )]
    public static partial void Applied(ILogger logger, string context);
}
