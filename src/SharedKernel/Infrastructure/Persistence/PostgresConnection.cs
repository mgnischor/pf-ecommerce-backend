using Npgsql;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Builds the single <see cref="NpgsqlDataSource"/> of the application (ai/DATABASE.md §7.2).</summary>
internal static class PostgresConnection
{
    /// <summary>Application name PostgreSQL reports in <c>pg_stat_activity</c>.</summary>
    public const string ApplicationName = "pf-ecommerce-api";

    /// <summary>Creates the data source with explicit pool and timeout settings.</summary>
    /// <param name="connectionString">The secret connection string.</param>
    /// <param name="options">Pool and timeout settings; they override the connection string's.</param>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    public static NpgsqlDataSource CreateDataSource(string? connectionString, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The message names the setting, never a value.
            throw new InvalidOperationException(
                $"The connection string '{DatabaseOptions.ConnectionStringName}' is not configured "
                    + "(set ConnectionStrings__Postgres or mount the secret)."
            );
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = options.MaxPoolSize,
            CommandTimeout = options.CommandTimeoutSeconds,
            Timeout = options.ConnectionTimeoutSeconds,
            ApplicationName = ApplicationName,
        };

        return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
    }
}
