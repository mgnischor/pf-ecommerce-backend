using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The one place that decides how a context talks to PostgreSQL, shared by the running application and the
/// design-time factories so migrations are generated against exactly the model that runs.
/// </summary>
internal static class PostgresContextOptions
{
    /// <summary>Configures a context for a shared data source.</summary>
    /// <param name="builder">Options builder.</param>
    /// <param name="schema">Schema (and migration-history schema) of the context.</param>
    /// <param name="dataSource">Shared data source.</param>
    /// <param name="options">Resilience settings.</param>
    public static void Configure(
        DbContextOptionsBuilder builder,
        string schema,
        NpgsqlDataSource dataSource,
        DatabaseOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        builder
            .UseNpgsql(
                dataSource,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(ModuleDbContext.MigrationsHistoryTable, schema);
                    npgsql.CommandTimeout(options.CommandTimeoutSeconds);

                    // Transient failures (dropped connection, failover) retry the whole unit with back-off and jitter.
                    npgsql.EnableRetryOnFailure(
                        options.MaxRetryCount,
                        TimeSpan.FromSeconds(options.MaxRetryDelaySeconds),
                        errorCodesToAdd: null
                    );
                }
            )
            .UseSnakeCaseNamingConvention();
    }
}
