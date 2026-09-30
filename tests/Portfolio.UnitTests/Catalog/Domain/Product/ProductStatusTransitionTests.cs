using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Domain;

[Trait("Rule", "BR-CAT-003")]
public sealed class ProductStatusTransitionTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_activate_a_draft_product_and_raise_a_status_changed_event()
    {
        var product = ProductBuilder.New().Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = product.Activate(_clock);

        result.IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Active);
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductStatusChanged>();
        domainEvent.From.ShouldBe(ProductStatus.Draft);
        domainEvent.To.ShouldBe(ProductStatus.Active);
        domainEvent.AggregateId.ShouldBe(product.Id);
        domainEvent.AggregateVersion.ShouldBe(product.Version);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start.AddMinutes(1));
    }

    [Fact]
    public void Should_discontinue_an_active_product_and_raise_a_status_changed_event()
    {
        var product = ProductBuilder.New().WithStatus(ProductStatus.Active).Build(_clock);

        var result = product.Discontinue(_clock);

        result.IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Discontinued);
        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductStatusChanged>();
        domainEvent.From.ShouldBe(ProductStatus.Active);
        domainEvent.To.ShouldBe(ProductStatus.Discontinued);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Discontinued")]
    public void Should_reject_activation_unless_the_product_is_a_draft(string currentStatus)
    {
        var status = Enum.Parse<ProductStatus>(currentStatus);
        var product = ProductBuilder.New().WithStatus(status).Build(_clock);
        var versionBefore = product.Version;

        var result = product.Activate(_clock);

        AssertRejectedTransition(result, product, status, ProductStatus.Active, versionBefore);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Discontinued")]
    public void Should_reject_discontinuation_unless_the_product_is_active(string currentStatus)
    {
        var status = Enum.Parse<ProductStatus>(currentStatus);
        var product = ProductBuilder.New().WithStatus(status).Build(_clock);
        var versionBefore = product.Version;

        var result = product.Discontinue(_clock);

        AssertRejectedTransition(result, product, status, ProductStatus.Discontinued, versionBefore);
    }

    [Fact]
    public void Should_walk_the_whole_lifecycle_raising_one_event_per_transition_with_increasing_versions()
    {
        var product = ProductBuilder.New().Build(_clock);

        product.Activate(_clock);
        product.Discontinue(_clock);

        product.DomainEvents.Select(e => e.AggregateVersion).ShouldBe([2, 3]);
    }

    private static void AssertRejectedTransition(
        Result result,
        Product product,
        ProductStatus current,
        ProductStatus requested,
        int versionBefore
    )
    {
        result.IsFailure.ShouldBeTrue();
        result.ShouldFail().Code.ShouldBe("PRODUCT_INVALID_STATUS_TRANSITION");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        result.ShouldFail().RuleId.ShouldBe("BR-CAT-003");
        result.ShouldFail().ShouldHaveParameters()["from"].ShouldBe(current.ToString());
        result.ShouldFail().ShouldHaveParameters()["to"].ShouldBe(requested.ToString());
        product.Status.ShouldBe(current);
        product.Version.ShouldBe(versionBefore);
        product.DomainEvents.ShouldBeEmpty();
    }
}
