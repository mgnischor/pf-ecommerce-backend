using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Base of the <see cref="DbContext"/> every bounded context owns (ai/DATABASE.md §5): one PostgreSQL schema per
/// context, its own migration history, the shared entity mapping, and the outbox and inbox tables of that schema.
/// It is the context's unit of work: a use case ends with one <c>SaveChangesAsync</c>,
/// which commits the aggregate, its outbox rows, and its inbox row atomically.
/// </summary>
/// <param name="options">Context options, built by <see cref="PostgresServiceCollectionExtensions"/>.</param>
/// <param name="schema">PostgreSQL schema the context owns, in <c>snake_case</c>.</param>
internal abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options), IUnitOfWork
{
    /// <summary>PostgreSQL error code of a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>Name of the migration history table, in the context's own schema.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>The schema the context owns.</summary>
    public string Schema { get; } = schema;

    /// <summary>Domain events waiting to be published.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Messages already handled by a consumer.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// Commits the staged changes. A lost optimistic-concurrency race or a unique-key race becomes a
    /// <see cref="PersistenceConflictException"/> (answered with <c>409</c>) instead of a provider exception.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">Whether to reset change tracking after the commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConflictException(PersistenceConflictKind.ConcurrentUpdate, exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new PersistenceConflictException(PersistenceConflictKind.DuplicateRecord, exception);
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        // A context's mappings live in its own Infrastructure namespace, so contexts never pick up each other's.
        var mappingNamespace = GetType().Namespace;
        modelBuilder.ApplyConfigurationsFromAssembly(
            GetType().Assembly,
            type => string.Equals(type.Namespace, mappingNamespace, StringComparison.Ordinal)
        );

        modelBuilder.ApplyEntityConventions();
    }
}
