using System.Reflection;
using NetArchTest.Rules;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// Guards the fitness functions themselves: a namespace rule that selects no type passes vacuously,
/// so a typo or a moved namespace would silently disable it.
/// </summary>
public sealed class RuleCoverageTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    [Theory]
    [InlineData(@"^Portfolio\.SharedKernel\.Domain(\.|$)")]
    [InlineData(@"^Portfolio\.SharedKernel\.Application(\.|$)")]
    [InlineData(@"^Portfolio\.Catalog\.Domain(\.|$)")]
    [InlineData(@"^Portfolio\.Catalog\.Application(\.|$)")]
    [InlineData(@"^Portfolio\.Catalog\.API\.Controllers$")]
    [InlineData(@"^Portfolio\.Catalog\.API\.Contracts$")]
    [InlineData(@"^Portfolio\.SharedKernel\.API(\.|$)")]
    public void Namespace_patterns_used_by_the_rules_should_select_existing_types(string pattern)
    {
        var selected = Types.InAssembly(App).That().ResideInNamespaceMatching(pattern).GetTypes();

        selected.ShouldNotBeEmpty();
    }

    [Fact]
    public void Layer_helpers_should_build_patterns_that_select_the_catalog_domain()
    {
        var selected = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(Namespace.Layer("Catalog", "Domain"))
            .GetTypes();

        selected.ShouldNotBeEmpty();
    }

    [Fact]
    public void Context_helper_should_build_a_pattern_that_selects_the_whole_catalog_context()
    {
        var selected = Types.InAssembly(App).That().ResideInNamespaceMatching(Namespace.Context("Catalog")).GetTypes();

        selected.Select(type => type.Namespace).ShouldContain("Portfolio.Catalog.Application", StringComparer.Ordinal);
    }
}
