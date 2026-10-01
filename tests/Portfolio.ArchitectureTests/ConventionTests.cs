using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using Portfolio.SharedKernel.API;
using Portfolio.SharedKernel.API.Authorization;
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
        // Infrastructure and API may define framework handlers (authorization or authentication handlers); the rule is about use cases.
        var handlers = Types
            .InAssembly(App)
            .That()
            .HaveNameEndingWith("Handler", StringComparison.Ordinal)
            .And()
            .DoNotResideInNamespaceMatching(@"\.(Infrastructure|API)(\.|$)");

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

    [Fact]
    public void Every_endpoint_should_declare_its_authorization_explicitly()
    {
        // Guards the rule itself: a scan that finds no endpoints would pass vacuously.
        Endpoints().Count().ShouldBeGreaterThan(30);

        var undeclared = Endpoints()
            .Where(endpoint =>
                !HasAuthorizationMetadata(endpoint.Controller) && !HasAuthorizationMetadata(endpoint.Action)
            )
            .Select(endpoint => $"{endpoint.Controller.Name}.{endpoint.Action.Name}")
            .ToArray();

        undeclared.ShouldBeEmpty(
            "Every action needs [Authorize(Policy = ...)] or an explicit [AllowAnonymous]; the fallback policy is a safety net, not a declaration."
        );
    }

    [Fact]
    public void Authorization_should_use_the_access_level_policies_and_never_roles()
    {
        var known = typeof(AccessPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string?)field.GetRawConstantValue())
            .ToHashSet(StringComparer.Ordinal);

        var offenders = Endpoints()
            .SelectMany(endpoint =>
                endpoint
                    .Controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Concat(endpoint.Action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                    .Where(attribute =>
                        !string.IsNullOrEmpty(attribute.Roles)
                        || attribute.Policy is null
                        || !known.Contains(attribute.Policy)
                    )
                    .Select(_ => $"{endpoint.Controller.Name}.{endpoint.Action.Name}")
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Access_levels_should_be_the_five_documented_levels_in_ascending_order()
    {
        Enum.GetNames<AccessLevel>().ShouldBe(["Public", "Collaborator", "Manager", "Administrator", "Developer"]);
        Enum.GetValues<AccessLevel>().Select(level => (int)level).ShouldBe([0, 1, 2, 3, 4]);
    }

    [Fact]
    public void Every_access_level_should_have_a_policy_except_public_which_is_anonymous_access()
    {
        typeof(AccessPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name)
            .ShouldBe(["Authenticated", "Collaborator", "Manager", "Administrator", "Developer"]);
    }

    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints() =>
        App.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller =>
                controller
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(action => action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                    .Select(action => (controller, action))
            );

    private static bool HasAuthorizationMetadata(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(attribute => attribute is IAuthorizeData or IAllowAnonymous);

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
