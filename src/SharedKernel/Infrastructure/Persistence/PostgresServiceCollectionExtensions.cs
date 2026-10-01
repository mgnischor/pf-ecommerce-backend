using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Composition of the PostgreSQL access (ai/DATABASE.md §7.1–§7.2): one shared <see cref="NpgsqlDataSource"/> with
/// explicit pool and timeout settings, and one pooled <see cref="DbContext"/> per bounded context that uses it,
/// each with snake_case naming, its own migration history, a retrying execution strategy, and the interceptors
/// that keep traceability and the outbox true.
/// </summary>
internal static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the data source, its settings, the interceptors, and the readiness check. The connection string
    /// is the secret <c>ConnectionStrings:Postgres</c>; the host refuses to start without it.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment; only Development may migrate at startup.</param>
    public static IServiceCollection AddPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var connectionString = provider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString(DatabaseOptions.ConnectionStringName);
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            return PostgresConnection.CreateDataSource(connectionString, options);
        });

        services.AddSingleton<AuditingSaveChangesInterceptor>();
        services.AddSingleton<OutboxSaveChangesInterceptor>();

        services.AddHealthChecks().AddCheck<PostgresHealthCheck>(PostgresHealthCheck.Name, tags: ["ready"]);

        if (
            environment.IsDevelopment()
            && configuration.GetValue<bool>($"{DatabaseOptions.SectionName}:MigrateOnStartup")
        )
        {
            // Registered before every context's own start-up work, so the schema exists when it runs.
            services.AddHostedService<DevelopmentDatabaseMigrator>();
        }

        return services;
    }

    /// <summary>Registers a bounded context's pooled <see cref="DbContext"/> and records it for migration.</summary>
    /// <typeparam name="TContext">The context's <see cref="ModuleDbContext"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="schema">PostgreSQL schema the context owns.</param>
    /// <param name="environment">Host environment; detailed errors only in Development.</param>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string schema,
        IHostEnvironment environment
    )
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddDbContextPool<TContext>(
            (provider, builder) =>
            {
                var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                PostgresContextOptions.Configure(
                    builder,
                    schema,
                    provider.GetRequiredService<NpgsqlDataSource>(),
                    options
                );
                builder.AddInterceptors(
                    provider.GetRequiredService<AuditingSaveChangesInterceptor>(),
                    provider.GetRequiredService<OutboxSaveChangesInterceptor>()
                );

                // Parameter values may carry personal data: never outside local development (ai/DATABASE.md §10.3).
                builder.EnableDetailedErrors(environment.IsDevelopment());
            }
        );

        services.AddSingleton(new MigratableContext(typeof(TContext)));

        return services;
    }

    /// <summary>
    /// Registers a context's use cases with that context as their unit of work. Every handler takes the
    /// <see cref="IUnitOfWork"/> of <em>its</em> context, and there is deliberately no global registration:
    /// with several <see cref="DbContext"/>s a single shared <see cref="IUnitOfWork"/> would commit the wrong one.
    /// </summary>
    /// <typeparam name="TContext">The context that commits the use case.</typeparam>
    /// <typeparam name="THandler">Use case class; its <see cref="IUnitOfWork"/> parameter receives <typeparamref name="TContext"/>.</typeparam>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddUseCase<TContext, THandler>(this IServiceCollection services)
        where TContext : ModuleDbContext
        where THandler : class
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddScoped(provider =>
            ActivatorUtilities.CreateInstance<THandler>(provider, provider.GetRequiredService<TContext>())
        );
    }
}
