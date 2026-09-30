using System.Reflection;
using NetArchTest.Rules;
using Portfolio.SharedKernel.API;
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
        // Infrastructure may define framework handlers (for example authentication handlers); the rule is about use cases.
        var handlers = Types
            .InAssembly(App)
            .That()
            .HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .And()
            .DoNotResideInNamespaceMatching(@"\.Infrastructure(\.|$)");

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
    public void Types_outside_the_api_layer_should_be_internal_unless_they_are_a_documented_public_contract()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.")
            .And()
            .DoNotResideInNamespaceMatching(@"\.API(\.|$)") // MVC only discovers public controllers; contracts are their signatures.
            .And()
            .DoNotHaveName(nameof(DomainException)) // Sonar S3871 requires exceptions to be public.
            .Should()
            .NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Controllers_should_be_sealed_api_controllers_living_in_the_api_layer()
    {
        var controllers = Types.InAssembly(App).That().Inherit(typeof(ApiControllerBase));

        var sealedResult = controllers.Should().BeSealed().GetResult();
        var layerResult = controllers
            .Should()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.API\.Controllers$")
            .GetResult();
        var nameResult = controllers.Should().HaveNameEndingWith("Controller", StringComparison.Ordinal).GetResult();

        sealedResult.IsSuccessful.ShouldBeTrue(Describe(sealedResult));
        layerResult.IsSuccessful.ShouldBeTrue(Describe(layerResult));
        nameResult.IsSuccessful.ShouldBeTrue(Describe(nameResult));
    }

    [Fact]
    public void Every_controller_should_derive_from_the_shared_api_controller_base()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .HaveNameEndingWith("Controller", StringComparison.Ordinal)
            .And()
            .DoNotHaveName(nameof(ApiControllerBase))
            .Should()
            .Inherit(typeof(ApiControllerBase))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Api_contracts_should_live_in_the_contracts_namespace_and_not_expose_domain_types()
    {
        var result = Types
            .InAssembly(App)
            .That()
            .ResideInNamespaceMatching(@"^Portfolio\.\w+\.API\.Contracts$")
            .ShouldNot()
            .HaveDependencyOnAny([.. Contexts.Names.Select(context => $"Portfolio.{context}.Domain")])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
