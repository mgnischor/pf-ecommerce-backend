using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Catalog.Domain;

[Trait("Rule", "BR-CAT-001")]
public sealed class ProductUpdateTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_change_name_and_description_as_one_change_that_advances_the_version_once()
    {
        var product = ProductBuilder.New().Build(_clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = product.Update(true, "  Nova cafeteira  ", true, "  Nova descrição ", _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Name.ShouldBe("Nova cafeteira");
        product.Description.ShouldBe("Nova descrição");
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        product.UpdatedAt.ShouldBe(TestClock.Start.AddMinutes(1));
    }

    [Fact]
    public void Should_leave_a_member_the_request_did_not_name_unchanged()
    {
        var product = ProductBuilder.New().WithName("Cafeteira").WithDescription("Descrição original").Build(_clock);

        var result = product.Update(true, "Cafeteira Premium", false, "ignored", _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Name.ShouldBe("Cafeteira Premium");
        product.Description.ShouldBe("Descrição original");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Should_clear_the_description_when_it_is_named_blank(string? description)
    {
        var product = ProductBuilder.New().WithDescription("Descrição original").Build(_clock);

        var result = product.Update(false, null, true, description, _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Description.ShouldBeNull();
        product.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    public void Should_change_nothing_when_the_request_holds_what_the_product_already_has()
    {
        var product = ProductBuilder.New().WithName("Cafeteira").WithDescription("Descrição").Build(_clock);

        var result = product.Update(true, " Cafeteira ", true, "Descrição", _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
        product.UpdatedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_change_nothing_when_the_request_names_no_member()
    {
        var product = ProductBuilder.New().Build(_clock);

        var result = product.Update(false, null, false, null, _clock);

        result.IsSuccess.ShouldBeTrue();
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Theory]
    [InlineData(null, "PRODUCT_NAME_REQUIRED")]
    [InlineData("ab", "PRODUCT_NAME_LENGTH")]
    public void Should_reject_an_invalid_name_and_apply_nothing_even_when_the_description_is_valid(
        string? name,
        string expectedCode
    )
    {
        var product = ProductBuilder.New().WithDescription("Descrição original").Build(_clock);

        var result = product.Update(true, name, true, "Outra descrição", _clock);

        result.ShouldFail().Code.ShouldBe(expectedCode);
        product.Description.ShouldBe("Descrição original");
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Fact]
    public void Should_reject_a_description_that_is_too_long_and_apply_nothing_even_when_the_name_is_valid()
    {
        var product = ProductBuilder.New().WithName("Cafeteira").Build(_clock);

        var result = product.Update(
            true,
            "Cafeteira Premium",
            true,
            new string('x', Product.MaxDescriptionLength + 1),
            _clock
        );

        result.ShouldFail().Code.ShouldBe("PRODUCT_DESCRIPTION_TOO_LONG");
        product.Name.ShouldBe("Cafeteira");
        product.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Fact]
    public void Should_not_raise_a_domain_event_on_update()
    {
        var product = ProductBuilder.New().Build(_clock);

        product.Update(true, "Nova cafeteira", false, null, _clock);

        product.DomainEvents.ShouldBeEmpty();
    }
}

[Trait("Rule", "BR-CAT-007")]
public sealed class ProductDeletionTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Theory]
    [InlineData("Draft")]
    [InlineData("Active")]
    [InlineData("Discontinued")]
    public void Should_logically_delete_a_product_in_every_status_and_keep_the_deletion_key(string statusName)
    {
        var product = ProductBuilder.New().WithStatus(Enum.Parse<ProductStatus>(statusName)).Build(_clock);
        var versionBefore = product.Version;
        _clock.Advance(TimeSpan.FromHours(1));

        product.Delete("key-0001", _clock);

        product.IsDeleted.ShouldBeTrue();
        product.DeletedAt.ShouldBe(TestClock.Start.AddHours(1));
        product.DeletionKey.ShouldBe("key-0001");
        product.Version.ShouldBe(versionBefore + 1);
    }

    [Fact]
    public void Should_raise_a_single_deleted_event_carrying_the_released_sku()
    {
        var product = ProductBuilder.New().WithSku("CAF-600-PRT").WithStatus(ProductStatus.Active).Build(_clock);
        _clock.Advance(TimeSpan.FromHours(1));

        product.Delete("key-0001", _clock);

        var domainEvent = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductDeleted>();
        domainEvent.AggregateId.ShouldBe(product.Id);
        domainEvent.AggregateVersion.ShouldBe(product.Version);
        domainEvent.OccurredAt.ShouldBe(TestClock.Start.AddHours(1));
        domainEvent.Sku.ShouldBe("CAF-600-PRT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_refuse_to_delete_without_an_idempotency_key(string? key)
    {
        var product = ProductBuilder.New().Build(_clock);

        Should.Throw<ArgumentException>(() => product.Delete(key!, _clock));

        product.IsDeleted.ShouldBeFalse();
    }
}

[Trait("Rule", "BR-CAT-008")]
public sealed class ProductCreationKeyTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private static Money Price(decimal amount = 189.90m) => new(amount, "BRL");

    private static Sku NewSku(string value = "CAF-600-PRT") => Sku.Create(value).Value;

    [Fact]
    public void Should_keep_the_creation_key_it_was_created_with()
    {
        var product = Product.Create("Cafeteira", NewSku(), Price(), null, _clock, "key-0001").Value;

        product.CreationKey.ShouldBe("key-0001");
        product.DeletionKey.ShouldBeNull();
    }

    [Fact]
    public void Should_have_no_creation_key_when_none_was_sent()
    {
        Product.Create("Cafeteira", NewSku(), Price(), null, _clock).Value.CreationKey.ShouldBeNull();
    }

    [Fact]
    public void Should_recognise_the_request_that_created_it_ignoring_padding_and_blank_descriptions()
    {
        var product = Product.Create("Cafeteira", NewSku(), Price(), "  ", _clock, "key-0001").Value;

        product.WasCreatedFrom(NewSku("caf-600-prt"), "  Cafeteira ", Price(), null).ShouldBeTrue();
    }

    [Theory]
    [InlineData("OTHER-SKU", "Cafeteira", 189.90, null)]
    [InlineData("CAF-600-PRT", "Outra", 189.90, null)]
    [InlineData("CAF-600-PRT", "Cafeteira", 199.90, null)]
    [InlineData("CAF-600-PRT", "Cafeteira", 189.90, "Com descrição")]
    public void Should_not_recognise_a_different_request(string sku, string name, double price, string? description)
    {
        var product = Product.Create("Cafeteira", NewSku(), Price(), null, _clock, "key-0001").Value;

        product.WasCreatedFrom(NewSku(sku), name, Price((decimal)price), description).ShouldBeFalse();
    }
}
