using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Options for <c>dotnet ef</c> (migrations, scripts, the migrations bundle), which build a context without the host.
/// They go through <see cref="PostgresContextOptions"/>, so the generated model is the one that runs. Nothing here
/// connects while a script or a bundle is produced. A running bundle takes its connection from <c>--connection</c>,
/// or, when it must not appear on a command line (a container job), from the secret file named by
/// <see cref="ConnectionFileVariable"/>.
/// </summary>
internal static class DesignTimeOptions
{
    /// <summary>Environment variable that points the tools at a database (for example <c>database update</c>).</summary>
    public const string ConnectionVariable = "PF_DESIGN_TIME_CONNECTION";

    /// <summary>Environment variable naming a file whose content is the connection string, so it is never an argument.</summary>
    public const string ConnectionFileVariable = "PF_DESIGN_TIME_CONNECTION_FILE";

    // Placeholder without credentials: it exists so the provider can be configured, never to connect.
    private const string OfflineConnection = "Host=localhost;Database=ecommerce;Username=design_time";

    /// <summary>Builds the options of a context for the EF Core tools.</summary>
    /// <typeparam name="TContext">The context.</typeparam>
    /// <param name="schema">Schema the context owns.</param>
    public static DbContextOptions<TContext> Create<TContext>(string schema)
        where TContext : ModuleDbContext
    {
        var dataSource = new NpgsqlDataSourceBuilder(ResolveConnection(Environment.GetEnvironmentVariable)).Build();

        var builder = new DbContextOptionsBuilder<TContext>();
        PostgresContextOptions.Configure(builder, schema, dataSource, new DatabaseOptions());
        return builder.Options;
    }

    /// <summary>
    /// Picks the connection: the secret file when named, else the variable, else the credential-free placeholder.
    /// </summary>
    /// <param name="environment">Reads an environment variable; injected so the choice can be tested.</param>
    internal static string ResolveConnection(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var file = environment(ConnectionFileVariable);
        if (!string.IsNullOrWhiteSpace(file))
        {
            return File.ReadAllText(file).Trim();
        }

        var connection = environment(ConnectionVariable);
        return string.IsNullOrWhiteSpace(connection) ? OfflineConnection : connection;
    }
}
