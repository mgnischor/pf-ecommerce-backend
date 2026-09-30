using System.Reflection;
using NetArchTest.Rules;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// Fitness functions for the dependency flow <c>API → Application → Domain ← Infrastructure</c>
/// (ai/ARCHITECTURE.md §6.2 and §10.2). Rules are namespace-based because every context shares one project.
/// </summary>
public sealed class DependencyDirectionTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "RabbitMQ.Client",
        "StackExchange.Redis",
        "OpenTelemetry",
        "Npgsql",
    ];

    [Theory]
    [MemberData(nameof(Contexts.All), MemberType = typeof(Contexts))]
    public void Domain_should_not_depend_on_other_layers_or_frameworks(string context)
    {
        var forbidden = Frameworks
            .Concat([
                $"Portfolio.{context}.Application",
                $"Portfolio.{context}.Infrastructure",
                $"Portfolio.{context}.API",
            ])
            .ToArray();

        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(Namespace.Layer(context, "Domain"))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(Contexts.All), MemberType = typeof(Contexts))]
    public void Application_should_not_depend_on_infrastructure_api_or_frameworks(string context)
    {
        var forbidden = Frameworks
            .Concat([$"Portfolio.{context}.Infrastructure", $"Portfolio.{context}.API"])
            .ToArray();

        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(Namespace.Layer(context, "Application"))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Shared_kernel_should_not_depend_on_any_bounded_context_or_framework()
    {
        var forbidden = Frameworks.Concat(Contexts.Names.Select(context => $"Portfolio.{context}")).ToArray();

        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(Namespace.Context("SharedKernel"))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(Contexts.All), MemberType = typeof(Contexts))]
    public void Context_should_not_depend_on_another_contexts_types(string context)
    {
        var others = Contexts
            .Names.Where(other => !string.Equals(other, context, StringComparison.Ordinal))
            .Select(other => $"Portfolio.{other}")
            .ToArray();

        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(Namespace.Context(context))
            .ShouldNot()
            .HaveDependencyOnAny(others)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
