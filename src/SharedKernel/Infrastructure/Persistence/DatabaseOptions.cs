namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Connection and resilience settings of the PostgreSQL access (ai/DATABASE.md §7.2, §10.5). The connection string
/// itself is the secret <c>ConnectionStrings:Postgres</c> and never lives in these options.
/// </summary>
internal sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Name of the connection string (<c>ConnectionStrings:Postgres</c>).</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>Largest number of pooled connections. <c>replicas × MaxPoolSize</c> must stay below the server budget.</summary>
    public int MaxPoolSize { get; init; } = 20;

    /// <summary>Seconds a command may run before it is cancelled.</summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Seconds to wait for a connection from the pool or a new one.</summary>
    public int ConnectionTimeoutSeconds { get; init; } = 15;

    /// <summary>Retries of a transient failure (connection loss, failover) before the error surfaces.</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>Upper bound in seconds of the exponential back-off between retries.</summary>
    public int MaxRetryDelaySeconds { get; init; } = 5;

    /// <summary>
    /// Applies pending migrations when the host starts. Honoured in the <c>Development</c> environment only:
    /// everywhere else migrations run through the migrations bundle (ai/DATABASE.md §6.1).
    /// </summary>
    public bool MigrateOnStartup { get; init; }
}
