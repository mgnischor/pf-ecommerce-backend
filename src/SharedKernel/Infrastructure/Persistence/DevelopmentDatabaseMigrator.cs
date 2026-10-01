using Microsoft.EntityFrameworkCore;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Applies every registered context's pending migrations when the host starts. Only ever registered in the
/// Development environment with <c>Database:MigrateOnStartup</c> set: everywhere else migrations run through the
/// migrations bundle, with the DDL-capable role, before the new version rolls out (ai/DATABASE.md §6.1).
/// </summary>
internal sealed class DevelopmentDatabaseMigrator(
    IServiceScopeFactory scopes,
    IEnumerable<MigratableContext> contexts,
    ILogger<DevelopmentDatabaseMigrator> logger
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var contextType in contexts.Select(registration => registration.ContextType))
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.Database.MigrateAsync(cancellationToken);
            MigratorLog.Applied(logger, contextType.Name);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
