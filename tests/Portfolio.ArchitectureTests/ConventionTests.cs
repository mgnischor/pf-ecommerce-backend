using System.Reflection;
using NetArchTest.Rules;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.ArchitectureTests;

/// <summary>Naming and visibility conventions (ai/ARCHITECTURE.md §10.2, ai/CODE.md §4.1).</summary>
public sealed class ConventionTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    // Past-tense verbs that do not end in "ed" (ai/ARCHITECTURE.md §4.4: events are named in the past tense).
    private const string PastTenseName = "(ed|Paid|Sent|Set|Made|Lost|Built)$";

    [Fact]
    public void Domain_events_should_be_sealed()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Domain_events_should_be_named_in_the_past_tense()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .And()
            .ResideInNamespaceMatching(@"^Portfolio\.(?!UnitTests)")
            .Should()
            .HaveNameMatching(PastTenseName)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Handlers_should_be_sealed_classes_in_the_application_layer()
    {
        var handlers = Types.InAssembly(App).That().HaveNameEndingWith("Handler", StringComparison.Ordinal);

        var sealedResult = handlers.Should().BeSealed().GetResult();
        var layerResult = handlers
            .Should()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.Application(\.|$)")
            .GetResult();

        sealedResult.IsSuccessful.ShouldBeTrue(Describe(sealedResult));
        layerResult.IsSuccessful.ShouldBeTrue(Describe(layerResult));
    }

    [Fact]
    public void Repository_interfaces_should_be_defined_in_the_domain_layer()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .AreInterfaces()
            .And()
            .HaveNameMatching("^I\\w+Repository$")
            .Should()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.Domain(\.|$)")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Types_should_be_internal_unless_they_are_a_documented_public_contract()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.")
            .And()
            .DoNotHaveName(nameof(DomainException)) // Sonar S3871 requires exceptions to be public.
            .Should()
            .NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
