using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Cart.Domain;

[Trait("Rule", "BR-CRT-001")]
public sealed class ShoppingCartOpeningTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_open_an_empty_active_cart_for_the_customer()
    {
        var customer = Guid.CreateVersion7();

        var cart = ShoppingCart.Open(customer, "BRL", _clock).Value;

        cart.CustomerId.ShouldBe(customer);
        cart.Status.ShouldBe(CartStatus.Active);
        cart.Items.ShouldBeEmpty();
        cart.Version.ShouldBe(AggregateRoot.InitialVersion);
        cart.CreatedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_normalize_the_currency_to_uppercase()
    {
        ShoppingCart.Open(Guid.CreateVersion7(), "brl", _clock).Value.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("B2N")]
    [InlineData("REAIS")]
    public void Should_reject_an_invalid_currency(string? currency)
    {
        var result = ShoppingCart.Open(Guid.CreateVersion7(), currency, _clock);

        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
    }

    [Fact]
    public void Should_refuse_an_empty_customer_identifier()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ShoppingCart.Open(Guid.Empty, "BRL", _clock));
    }

    [Fact]
    public void Should_not_raise_domain_events()
    {
        ShoppingCart.Open(Guid.CreateVersion7(), "BRL", _clock).Value.DomainEvents.ShouldBeEmpty();
    }
}

public sealed class ShoppingCartItemTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly ShoppingCart _cart;

    public ShoppingCartItemTests()
    {
        _cart = ShoppingCart.Open(Guid.CreateVersion7(), "BRL", _clock).Value;
    }

    [Fact]
    [Trait("Rule", "BR-CRT-002")]
    public void Should_add_a_line_and_advance_the_version_once()
    {
        var product = Guid.CreateVersion7();
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = _cart.AddItem(product, 2, _clock);

        var line = result.Value;
        line.ProductId.ShouldBe(product);
        line.Quantity.ShouldBe(2);
        line.CartId.ShouldBe(_cart.Id);
        _cart.Items.ShouldHaveSingleItem().ShouldBe(line);
        _cart.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        _cart.UpdatedAt.ShouldBe(TestClock.Start.AddMinutes(1));
    }

    [Fact]
    [Trait("Rule", "BR-CRT-003")]
    public void Should_increase_the_existing_line_instead_of_adding_a_second_one_for_the_same_product()
    {
        var product = Guid.CreateVersion7();
        var first = _cart.AddItem(product, 2, _clock).Value;

        var second = _cart.AddItem(product, 3, _clock).Value;

        second.ShouldBe(first);
        _cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(5);
        _cart.Version.ShouldBe(AggregateRoot.InitialVersion + 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    [Trait("Rule", "BR-CRT-002")]
    public void Should_reject_a_quantity_outside_the_range_and_change_nothing(int quantity)
    {
        var result = _cart.AddItem(Guid.CreateVersion7(), quantity, _clock);

        var error = result.ShouldFail();
        error.Code.ShouldBe("CART_ITEM_QUANTITY_INVALID");
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe("quantity");
        _cart.Items.ShouldBeEmpty();
        _cart.Version.ShouldBe(AggregateRoot.InitialVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [Trait("Rule", "BR-CRT-002")]
    public void Should_accept_the_quantity_limits(int quantity)
    {
        _cart.AddItem(Guid.CreateVersion7(), quantity, _clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    [Trait("Rule", "BR-CRT-002")]
    public void Should_reject_taking_a_line_above_the_maximum_and_keep_its_quantity()
    {
        var product = Guid.CreateVersion7();
        _cart.AddItem(product, 90, _clock);

        var result = _cart.AddItem(product, 10, _clock);

        result.ShouldFail().Code.ShouldBe("CART_LINE_QUANTITY_LIMIT");
        _cart.Items.Single().Quantity.ShouldBe(90);
        _cart.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-002")]
    public void Should_allow_a_line_to_reach_the_maximum_exactly()
    {
        var product = Guid.CreateVersion7();
        _cart.AddItem(product, 90, _clock);

        _cart.AddItem(product, 9, _clock).IsSuccess.ShouldBeTrue();

        _cart.Items.Single().Quantity.ShouldBe(ShoppingCart.MaxLineQuantity);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-003")]
    public void Should_reject_a_product_beyond_the_maximum_number_of_lines_but_still_grow_an_existing_one()
    {
        var first = Guid.CreateVersion7();
        _cart.AddItem(first, 1, _clock);
        for (var index = 1; index < ShoppingCart.MaxDistinctItems; index++)
        {
            _cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        }

        var overflow = _cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        var existing = _cart.AddItem(first, 1, _clock);

        overflow.ShouldFail().Code.ShouldBe("CART_ITEM_LIMIT");
        _cart.Items.Count.ShouldBe(ShoppingCart.MaxDistinctItems);
        existing.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Should_remove_a_line_and_advance_the_version()
    {
        var kept = _cart.AddItem(Guid.CreateVersion7(), 1, _clock).Value;
        var removed = _cart.AddItem(Guid.CreateVersion7(), 1, _clock).Value;
        var versionBefore = _cart.Version;

        var result = _cart.RemoveItem(removed.Id, _clock);

        result.IsSuccess.ShouldBeTrue();
        _cart.Items.ShouldHaveSingleItem().ShouldBe(kept);
        _cart.Version.ShouldBe(versionBefore + 1);
    }

    [Fact]
    public void Should_allow_a_removed_product_to_be_added_again_as_a_new_line()
    {
        var product = Guid.CreateVersion7();
        var original = _cart.AddItem(product, 5, _clock).Value;
        _cart.RemoveItem(original.Id, _clock);

        var again = _cart.AddItem(product, 1, _clock).Value;

        again.Id.ShouldNotBe(original.Id);
        again.Quantity.ShouldBe(1);
    }

    [Fact]
    public void Should_report_a_missing_line_without_changing_the_cart()
    {
        _cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        var versionBefore = _cart.Version;

        var result = _cart.RemoveItem(Guid.CreateVersion7(), _clock);

        var error = result.ShouldFail();
        error.Code.ShouldBe("CART_ITEM_NOT_FOUND");
        error.Type.ShouldBe(ErrorType.NotFound);
        _cart.Version.ShouldBe(versionBefore);
    }

    [Fact]
    public void Should_report_a_line_that_was_already_removed_as_missing()
    {
        var line = _cart.AddItem(Guid.CreateVersion7(), 1, _clock).Value;
        _cart.RemoveItem(line.Id, _clock);

        _cart.RemoveItem(line.Id, _clock).ShouldFail().Code.ShouldBe("CART_ITEM_NOT_FOUND");
    }
}

[Trait("Rule", "BR-CRT-004")]
public sealed class ShoppingCartLifecycleTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    private ShoppingCart NewCart(bool withItem = true)
    {
        var cart = ShoppingCart.Open(Guid.CreateVersion7(), "BRL", _clock).Value;
        if (withItem)
        {
            cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        }

        return cart;
    }

    [Fact]
    public void Should_check_out_an_active_cart_with_items()
    {
        var cart = NewCart();
        var versionBefore = cart.Version;

        var result = cart.MarkCheckedOut(_clock);

        result.IsSuccess.ShouldBeTrue();
        cart.Status.ShouldBe(CartStatus.CheckedOut);
        cart.Version.ShouldBe(versionBefore + 1);
    }

    [Fact]
    public void Should_refuse_to_check_out_an_empty_cart()
    {
        var cart = NewCart(withItem: false);

        var result = cart.MarkCheckedOut(_clock);

        result.ShouldFail().Code.ShouldBe("CART_EMPTY");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        cart.Status.ShouldBe(CartStatus.Active);
    }

    [Fact]
    public void Should_expire_an_active_cart()
    {
        var cart = NewCart(withItem: false);

        cart.Expire(_clock).IsSuccess.ShouldBeTrue();

        cart.Status.ShouldBe(CartStatus.Expired);
    }

    [Fact]
    public void Should_not_leave_a_terminal_status()
    {
        var checkedOut = NewCart();
        checkedOut.MarkCheckedOut(_clock);
        var expired = NewCart();
        expired.Expire(_clock);

        checkedOut.Expire(_clock).ShouldFail().Code.ShouldBe("CART_INVALID_STATUS_TRANSITION");
        checkedOut.MarkCheckedOut(_clock).ShouldFail().Code.ShouldBe("CART_INVALID_STATUS_TRANSITION");
        expired.MarkCheckedOut(_clock).ShouldFail().Code.ShouldBe("CART_INVALID_STATUS_TRANSITION");
        expired.Expire(_clock).ShouldFail().Code.ShouldBe("CART_INVALID_STATUS_TRANSITION");
        checkedOut.Status.ShouldBe(CartStatus.CheckedOut);
        expired.Status.ShouldBe(CartStatus.Expired);
    }

    [Theory]
    [InlineData("CheckedOut")]
    [InlineData("Expired")]
    public void Should_refuse_every_change_to_a_cart_that_is_no_longer_active(string status)
    {
        var cart = NewCart();
        var line = cart.Items.Single();
        _ = status == "CheckedOut" ? cart.MarkCheckedOut(_clock) : cart.Expire(_clock);
        var versionBefore = cart.Version;

        var add = cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        var remove = cart.RemoveItem(line.Id, _clock);

        add.ShouldFail().Code.ShouldBe("CART_NOT_ACTIVE");
        add.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        remove.ShouldFail().Code.ShouldBe("CART_NOT_ACTIVE");
        cart.Items.Count.ShouldBe(1);
        cart.Version.ShouldBe(versionBefore);
        cart.EnsureActive().ShouldFail().Code.ShouldBe("CART_NOT_ACTIVE");
    }

    [Fact]
    public void Should_report_an_active_cart_as_active()
    {
        NewCart().EnsureActive().IsSuccess.ShouldBeTrue();
    }
}
