using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NetArchTest.Rules;
using Portfolio.SharedKernel.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// The persistence rules of ai/DATABASE.md as fitness functions: one schema and one <c>DbContext</c> per context,
/// no entity of another context, the mandatory columns, soft delete and concurrency token on every entity, versioned
/// migrations, and no raw or concatenated SQL. The model is built offline (nothing connects).
/// </summary>
public sealed partial class PersistenceConventionTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    private static readonly string[] Technical = [nameof(OutboxMessage), nameof(InboxMessage)];

    /// <summary>Every <see cref="ModuleDbContext"/>, built through its design-time factory as <c>dotnet ef</c> builds it.</summary>
    public static TheoryData<string> Contexts()
    {
        var data = new TheoryData<string>();
        foreach (var type in DbContextTypes())
        {
            data.Add(ContextOf(type));
        }

        return data;
    }

    private static IEnumerable<Type> DbContextTypes() =>
        App.GetTypes().Where(type => type is { IsAbstract: false } && typeof(ModuleDbContext).IsAssignableFrom(type));

    private static string ContextOf(Type dbContext) => dbContext.Name[..^"DbContext".Length];

    private static Type DbContextOf(string context) => DbContextTypes().Single(type => ContextOf(type) == context);

    private static string SchemaOf(string context) =>
        (string)
            DbContextOf(context)
                .GetField("SchemaName", BindingFlags.Public | BindingFlags.Static)!
                .GetRawConstantValue()!;

    private static DbContext Build(string context)
    {
        var dbContext = DbContextOf(context);
        var factoryType = App.GetTypes()
            .Single(type =>
                typeof(Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<>)
                    .MakeGenericType(dbContext)
                    .IsAssignableFrom(type)
            );
        var factory = Activator.CreateInstance(factoryType)!;
        return (DbContext)factoryType.GetMethod("CreateDbContext")!.Invoke(factory, [Array.Empty<string>()])!;
    }

    private static IEnumerable<IEntityType> DomainEntities(DbContext context) =>
        context.Model.GetEntityTypes().Where(type => typeof(Entity).IsAssignableFrom(type.ClrType));

    [Fact]
    public void Guards_the_rules_themselves_by_finding_the_three_contexts_that_have_persistence()
    {
        DbContextTypes().Select(ContextOf).Order(StringComparer.Ordinal).ShouldBe(["Catalog", "Identity", "Inventory"]);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Each_context_should_have_exactly_one_db_context_in_its_own_infrastructure_namespace(string context)
    {
        DbContextOf(context).Namespace.ShouldBe($"Portfolio.{context}.Infrastructure");
        DbContextTypes().Count(type => ContextOf(type) == context).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Each_context_should_own_a_schema_named_after_it_in_snake_case(string context)
    {
        SchemaOf(context).ShouldBe(context.ToLowerInvariant());
        using var db = Build(context);

        db.Model.GetDefaultSchema().ShouldBe(SchemaOf(context));
        db.GetService<IDesignTimeModel>()
            .Model.GetEntityTypes()
            .Select(type => type.GetSchema())
            .ShouldAllBe(schema => schema == null || schema == SchemaOf(context));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void A_db_context_should_map_only_entities_of_its_own_context_plus_the_technical_tables(string context)
    {
        using var db = Build(context);

        var foreign = db
            .Model.GetEntityTypes()
            .Where(type => !Technical.Contains(type.ClrType.Name, StringComparer.Ordinal))
            .Where(type => !type.ClrType.Namespace!.StartsWith($"Portfolio.{context}.Domain", StringComparison.Ordinal))
            .Select(type => type.ClrType.FullName)
            .ToArray();

        foreign.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_domain_entity_of_a_context_should_be_mapped_by_its_db_context(string context)
    {
        using var db = Build(context);
        var domain = App.GetTypes()
            .Where(type => type is { IsAbstract: false } && typeof(Entity).IsAssignableFrom(type))
            .Where(type => type.Namespace!.StartsWith($"Portfolio.{context}.Domain", StringComparison.Ordinal))
            .ToArray();
        var mapped = db.Model.GetEntityTypes().Select(type => type.ClrType).ToHashSet();

        domain.ShouldNotBeEmpty();
        domain.Where(type => !mapped.Contains(type)).Select(type => type.Name).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_entity_should_carry_the_mandatory_traceability_columns_in_utc(string context)
    {
        using var db = Build(context);
        var model = db.GetService<IDesignTimeModel>().Model;

        foreach (var entity in model.GetEntityTypes().Where(type => typeof(Entity).IsAssignableFrom(type.ClrType)))
        {
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            var columns = entity
                .GetProperties()
                .ToDictionary(
                    property => property.Name,
                    property => property.GetColumnName(table),
                    StringComparer.Ordinal
                );

            columns[nameof(Entity.Id)].ShouldBe("id", entity.ClrType.Name);
            columns[nameof(Entity.CreatedAt)].ShouldBe("created_at", entity.ClrType.Name);
            columns[nameof(Entity.UpdatedAt)].ShouldBe("updated_at", entity.ClrType.Name);
            columns[nameof(Entity.DeletedAt)].ShouldBe("deleted_at", entity.ClrType.Name);
            foreach (var name in new[] { nameof(Entity.CreatedAt), nameof(Entity.UpdatedAt), nameof(Entity.DeletedAt) })
            {
                entity.FindProperty(name)!.GetColumnType().ShouldBe("timestamptz", $"{entity.ClrType.Name}.{name}");
            }

            entity
                .FindProperty(nameof(Entity.Id))!
                .ValueGenerated.ShouldBe(ValueGenerated.Never, "ids are generated in the domain");
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_aggregate_root_should_use_its_version_as_the_concurrency_token(string context)
    {
        using var db = Build(context);

        var roots = DomainEntities(db).Where(type => typeof(AggregateRoot).IsAssignableFrom(type.ClrType)).ToArray();

        roots.ShouldNotBeEmpty();
        foreach (var root in roots)
        {
            var version = root.FindProperty(nameof(AggregateRoot.Version)).ShouldNotBeNull(root.ClrType.Name);
            version.IsConcurrencyToken.ShouldBeTrue(root.ClrType.Name);
            version.IsNullable.ShouldBeFalse(root.ClrType.Name);
        }

        DomainEntities(db)
            .Where(type => !typeof(AggregateRoot).IsAssignableFrom(type.ClrType))
            .ShouldAllBe(type => type.FindProperty(nameof(AggregateRoot.Version)) == null);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_entity_should_hide_soft_deleted_rows_through_the_named_global_query_filter(string context)
    {
        using var db = Build(context);

        foreach (var entity in DomainEntities(db))
        {
            entity
                .GetDeclaredQueryFilters()
                .Select(filter => filter.Key)
                .ShouldContain(EntityModelConventions.SoftDeleteFilter, entity.ClrType.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_context_should_have_its_own_outbox_and_inbox_tables_in_its_schema(string context)
    {
        using var db = Build(context);

        db.Model.FindEntityType(typeof(OutboxMessage))!.GetTableName().ShouldBe("outbox_messages");
        db.Model.FindEntityType(typeof(InboxMessage))!.GetTableName().ShouldBe("inbox_messages");
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_entity_should_name_its_table_and_columns_in_snake_case(string context)
    {
        using var db = Build(context);
        var model = db.GetService<IDesignTimeModel>().Model;

        foreach (var entity in model.GetEntityTypes())
        {
            entity.GetTableName().ShouldNotBeNull().ShouldMatch(SnakeCase().ToString(), entity.ClrType.Name);
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;
            foreach (var property in entity.GetProperties())
            {
                property
                    .GetColumnName(table)
                    .ShouldNotBeNull()
                    .ShouldMatch(SnakeCase().ToString(), $"{entity.ClrType.Name}.{property.Name}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_money_column_should_be_a_decimal_never_floating_point(string context)
    {
        using var db = Build(context);

        var floating = db
            .Model.GetEntityTypes()
            .SelectMany(entity =>
                entity
                    .GetProperties()
                    .Concat(entity.GetComplexProperties().SelectMany(complex => complex.ComplexType.GetProperties()))
            )
            .Where(property => property.ClrType == typeof(double) || property.ClrType == typeof(float))
            .Select(property => property.Name)
            .ToArray();

        floating.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_context_should_ship_at_least_one_migration_and_a_design_time_factory(string context)
    {
        var dbContext = DbContextOf(context);

        var migrations = App.GetTypes()
            .Where(type =>
                typeof(Migration).IsAssignableFrom(type)
                && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == dbContext
            )
            .ToArray();

        migrations.ShouldNotBeEmpty();
        migrations.ShouldAllBe(type => type.Namespace == $"Portfolio.{context}.Infrastructure.Persistence.Migrations");
        migrations.ShouldAllBe(type => type.GetCustomAttribute<MigrationAttribute>() != null);
        App.GetTypes()
            .Any(type =>
                typeof(Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<>)
                    .MakeGenericType(dbContext)
                    .IsAssignableFrom(type)
            )
            .ShouldBeTrue();
    }

    [Fact]
    public void Migration_names_should_describe_the_change_in_pascal_case_after_the_timestamp()
    {
        var names = App.GetTypes()
            .Where(type =>
                typeof(Migration).IsAssignableFrom(type) && type.GetCustomAttribute<MigrationAttribute>() != null
            )
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()!.Id)
            .ToArray();

        names.ShouldNotBeEmpty();
        names.ShouldAllBe(id => MigrationName().IsMatch(id));
    }

    [Fact]
    public void Domain_and_application_and_api_should_not_depend_on_the_persistence_infrastructure()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.(Domain|Application|API)(\.|$)")
            .ShouldNot()
            .HaveDependencyOn("Portfolio.SharedKernel.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violating types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- Source rules: what a type-level test cannot see -----------------------------------------------------

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Portfolio.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Portfolio.csproj not found above the test binaries.");
    }

    private static IEnumerable<(string Path, string Text)> Sources(string folder) =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), folder), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .Select(path => (path, File.ReadAllText(path)));

    [Theory]
    [InlineData("FromSqlRaw")]
    [InlineData("ExecuteSqlRaw")]
    [InlineData("ExecuteSqlCommand")]
    [InlineData("EnableSensitiveDataLogging")]
    public void Source_should_never_use_a_construct_the_database_standard_forbids(string construct)
    {
        Sources("src")
            .Where(file => file.Text.Contains(construct, StringComparison.Ordinal))
            .Select(file => file.Path)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_development_migrator_should_apply_migrations_from_application_code()
    {
        var offenders = Sources("src")
            .Where(file =>
                file.Text.Contains(".MigrateAsync(", StringComparison.Ordinal)
                || file.Text.Contains(".Migrate()", StringComparison.Ordinal)
            )
            .Select(file => Path.GetFileName(file.Path))
            .ToArray();

        offenders.ShouldBe(["DevelopmentDatabaseMigrator.cs"]);
    }

    [Fact]
    public void Source_should_not_hardcode_a_database_credential()
    {
        Sources("src")
            .Where(file => PasswordInConnectionString().IsMatch(file.Text))
            .Select(file => file.Path)
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void A_contexts_persistence_code_should_never_name_another_contexts_schema(string context)
    {
        var own = SchemaOf(context);
        var others = DbContextTypes().Select(ContextOf).Select(SchemaOf).Where(schema => schema != own).ToArray();
        var pattern = new Regex(
            $@"(?<![\w.]){string.Join("|", others)}\.[a-z_]+",
            RegexOptions.None,
            TimeSpan.FromSeconds(2)
        );

        var crossings = Sources($"src/{context}/Infrastructure")
            .SelectMany(file =>
                pattern.Matches(file.Text).Select(match => $"{Path.GetFileName(file.Path)}: {match.Value}")
            )
            .ToArray();

        crossings.ShouldBeEmpty();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"^\d{14}_[A-Z][A-Za-z0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MigrationName();

    // A connection-string literal (Host=...;...;Password=value), not any assignment to a variable called password.
    [GeneratedRegex(
        @"(?:Host|Server)\s*=[^""
]*Password\s*=\s*[^;""\s{$]+",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000
    )]
    private static partial Regex PasswordInConnectionString();
}
