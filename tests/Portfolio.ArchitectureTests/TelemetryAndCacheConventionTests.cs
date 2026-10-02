using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Portfolio.SharedKernel.Infrastructure;
using Portfolio.SharedKernel.Telemetry;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// The telemetry and cache rules of ai/OBSERVABILITY.md and ai/DATABASE.md §4 as fitness functions: no telemetry in the
/// domain, none directly in handlers, one naming scheme for sources and meters, no direct console output, and no cache
/// access that bypasses the failure-proof boundary.
/// </summary>
public sealed partial class TelemetryAndCacheConventionTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);

    [Fact]
    public void The_domain_should_contain_no_telemetry_and_no_logging()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.Domain(\.|$)")
            .ShouldNot()
            .HaveDependencyOnAny(
                "OpenTelemetry",
                "System.Diagnostics.ActivitySource",
                "System.Diagnostics.Metrics",
                "Microsoft.Extensions.Logging",
                "Portfolio.SharedKernel.Telemetry"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Handlers_should_not_create_spans_or_meters_directly()
    {
        // ai/OBSERVABILITY.md §3.3: use-case telemetry is added by the composition root, never inside a handler.
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.Application(\.|$)")
            .ShouldNot()
            .HaveDependencyOnAny(
                "OpenTelemetry",
                "System.Diagnostics.ActivitySource",
                "System.Diagnostics.Metrics",
                "Portfolio.SharedKernel.Telemetry"
            )
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Only_the_composition_root_and_infrastructure_should_know_the_opentelemetry_sdk()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.(Application|API)(\.|$)")
            .ShouldNot()
            .HaveDependencyOn("OpenTelemetry")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Every_telemetry_name_should_live_under_the_ecommerce_prefix()
    {
        var names = typeof(TelemetryNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field =>
                field.Name.EndsWith("Source", StringComparison.Ordinal)
                || field.Name.EndsWith("Meter", StringComparison.Ordinal)
            )
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        names.ShouldNotBeEmpty();
        names.ShouldAllBe(name => name.StartsWith("Ecommerce.", StringComparison.Ordinal));
    }

    [Fact]
    public void Sources_and_meters_should_be_created_from_the_registered_names_never_from_a_free_literal()
    {
        // An unregistered source or meter is silently dropped (ai/OBSERVABILITY.md §16.2): names come from TelemetryNames.
        var offenders = Sources("src")
            .Where(file => !file.Path.EndsWith("TelemetryNames.cs", StringComparison.Ordinal))
            .Where(file => LiteralSource().IsMatch(file.Text))
            .Select(file => Path.GetFileName(file.Path))
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Meters_should_come_from_the_meter_factory_so_they_are_disposed_with_the_host()
    {
        var offenders = Sources("src")
            .Where(file => file.Text.Contains("new Meter(", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.Path))
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Console.WriteLine")]
    [InlineData("Console.Write(")]
    [InlineData("Console.Error")]
    public void Application_code_should_never_write_to_the_console_directly(string construct)
    {
        // ai/OBSERVABILITY.md §16.2: output goes through ILogger, so it is structured, correlated and redacted.
        Sources("src")
            .Where(file => file.Text.Contains(construct, StringComparison.Ordinal))
            .Select(file => file.Path)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_cache_boundary_and_the_blocklist_should_touch_the_valkey_client_or_the_hybrid_cache()
    {
        // ai/DATABASE.md §4.2: a cache failure is swallowed at the boundary. Direct use would let it reach a request.
        var allowed = new[]
        {
            "ResilientCache",
            "ValkeyConnection",
            "ValkeyHealthCheck",
            "ValkeyServiceCollectionExtensions",
            "ValkeyRevokedTokenStore",
        };

        var offenders = Types
            .InAssembly(App)
            .That()
            .HaveDependencyOnAny(
                "StackExchange.Redis",
                "Microsoft.Extensions.Caching.Hybrid",
                "Microsoft.Extensions.Caching.Distributed"
            )
            .GetTypes()
            .Select(type => type.Name)
            .Where(name =>
                !allowed.Any(entry => name.StartsWith(entry, StringComparison.Ordinal)) && !name.StartsWith('<')
            )
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Cache_entries_should_declare_a_positive_ttl_and_a_versioned_key()
    {
        // Every CacheEntry defined in the codebase is checked: nothing is cached without an expiry (ai/DATABASE.md §4.1).
        var entries = App.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(CacheEntry))
            .Select(field => (CacheEntry)field.GetValue(null)!)
            .ToArray();

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(entry => entry.Ttl > TimeSpan.Zero && entry.Ttl <= TimeSpan.FromHours(1));
        entries.ShouldAllBe(entry => entry.Version >= 1);
        entries.ShouldAllBe(entry => entry.KeyFor("p", "id").EndsWith($":v{entry.Version}", StringComparison.Ordinal));
        entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count().ShouldBe(entries.Length);
    }

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

    [GeneratedRegex(
        @"new\s+ActivitySource\(\s*""|\.Create\(\s*""Ecommerce|\.Create\(\s*""[A-Za-z]",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000
    )]
    private static partial Regex LiteralSource();
}
