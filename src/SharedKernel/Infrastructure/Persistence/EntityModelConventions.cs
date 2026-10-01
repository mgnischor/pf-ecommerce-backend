using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The mapping every domain entity shares (ai/DATABASE.md §2): the key generated in the domain, the three UTC
/// traceability columns, the soft-delete query filter, and the <c>version</c> concurrency token of aggregate
/// roots. Applied once per context so no configuration class can forget a mandatory column.
/// </summary>
internal static class EntityModelConventions
{
    /// <summary>Name of the soft-delete query filter; audit flows disable it by this name.</summary>
    public const string SoftDeleteFilter = "soft_delete";

    /// <summary>Applies the shared mapping to every <see cref="Entity"/> type in the model.</summary>
    /// <param name="modelBuilder">Model under construction.</param>
    public static void ApplyEntityConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entityTypes = modelBuilder
            .Model.GetEntityTypes()
            .Where(type => typeof(Entity).IsAssignableFrom(type.ClrType))
            .ToList();

        foreach (var entityType in entityTypes)
        {
            ConfigureEntity(modelBuilder, entityType);
        }
    }

    private static void ConfigureEntity(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        var clrType = entityType.ClrType;
        var builder = modelBuilder.Entity(clrType);

        builder.Ignore(nameof(Entity.IsDeleted));
        builder.HasKey(nameof(Entity.Id));
        builder.Property(nameof(Entity.Id)).HasColumnName("id").ValueGeneratedNever();
        builder.Property(nameof(Entity.CreatedAt)).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(nameof(Entity.UpdatedAt)).HasColumnName("updated_at").HasColumnType("timestamptz");
        builder.Property(nameof(Entity.DeletedAt)).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.HasQueryFilter(SoftDeleteFilter, SoftDeletePredicate(clrType));

        if (!typeof(AggregateRoot).IsAssignableFrom(clrType))
        {
            return;
        }

        // The domain increments the version on every state change; EF Core compares it with the value read.
        builder.Ignore(nameof(AggregateRoot.DomainEvents));
        builder.Property(nameof(AggregateRoot.Version)).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.ToTable(table => table.HasCheckConstraint($"ck_{entityType.GetTableName()}_version", "version >= 1"));
    }

    private static LambdaExpression SoftDeletePredicate(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "entity");
        var deletedAt = Expression.Property(parameter, nameof(Entity.DeletedAt));
        var isActive = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTimeOffset?)));
        return Expression.Lambda(isActive, parameter);
    }
}
