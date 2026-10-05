using Portfolio.Cart.Application;
using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Cart.Application;

[Trait("Rule", "BR-CRT-001")]
public sealed class OpenCartHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShoppingCartRepository _carts = new();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly OpenCartHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public OpenCartHandlerTests()
    {
        _handler = new OpenCartHandler(_carts, _products, _unitOfWork, _clock);
    }

    private Task<Result<OpenedCart>> OpenAsync(string? currency = "BRL", Guid? customer = null) =>
        _handler.HandleAsync(
            new OpenCartCommand(customer ?? _customer, currency),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_create_an_empty_active_cart_for_a_customer_who_has_none()
    {
        var result = await OpenAsync();

        var opened = result.Value;
        opened.Created.ShouldBeTrue();
        opened.Cart.Status.ShouldBe(CartStatusView.Active);
        opened.Cart.Currency.ShouldBe("BRL");
        opened.Cart.Items.ShouldBeEmpty();
        opened.Cart.SubtotalAmount.ShouldBe(0m);
        opened.Cart.Version.ShouldBe(AggregateRoot.InitialVersion);
        opened.Cart.AllowedActions.ShouldBe([CartActions.AddItem]);
        _carts.Carts.ShouldHaveSingleItem().CustomerId.ShouldBe(_customer);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_return_the_cart_that_is_already_open_instead_of_creating_a_second_one()
    {
        var first = await OpenAsync();

        var again = await OpenAsync("brl");

        again.Value.Created.ShouldBeFalse();
        again.Value.Cart.Id.ShouldBe(first.Value.Cart.Id);
        _carts.Carts.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_refuse_a_cart_in_another_currency_while_one_is_open_and_say_which_currency_it_uses()
    {
        await OpenAsync("BRL");

        var other = await OpenAsync("USD");

        other.ShouldFail().Code.ShouldBe("CART_ALREADY_OPEN");
        other.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        other.ShouldFail().ShouldHaveParameters()["currency"].ShouldBe("BRL");
        _carts.Carts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Should_let_each_customer_have_a_cart_of_their_own()
    {
        var first = await OpenAsync(customer: _customer);

        var second = await OpenAsync(customer: Guid.CreateVersion7());

        second.Value.Created.ShouldBeTrue();
        second.Value.Cart.Id.ShouldNotBe(first.Value.Cart.Id);
    }

    [Fact]
    public async Task Should_open_a_new_cart_when_the_previous_one_was_checked_out()
    {
        var checkedOut = CartScenarios.Cart(_clock, _customer);
        checkedOut.AddItem(Guid.CreateVersion7(), 1, _clock);
        checkedOut.MarkCheckedOut(_clock);
        _carts.Seed(checkedOut);

        var result = await OpenAsync();

        result.Value.Created.ShouldBeTrue();
        result.Value.Cart.Id.ShouldNotBe(checkedOut.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BR")]
    [InlineData("B2N")]
    public async Task Should_reject_an_invalid_currency_and_persist_nothing(string? currency)
    {
        var result = await OpenAsync(currency);

        result.ShouldFail().Code.ShouldBe("MONEY_INVALID_CURRENCY");
        _carts.Carts.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}

[Trait("Rule", "BR-CRT-005")]
public sealed class GetCartHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShoppingCartRepository _carts = new();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly GetCartHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();

    public GetCartHandlerTests()
    {
        _handler = new GetCartHandler(_carts, _products);
    }

    private Task<Result<CartView>> GetAsync(Guid cartId, Guid? customer = null) =>
        _handler.HandleAsync(new GetCartQuery(customer ?? _customer, cartId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_price_every_line_from_the_catalog_view_and_sum_the_subtotal_on_the_server()
    {
        var cart = CartScenarios.Cart(_clock, _customer);
        var coffee = CartScenarios.Product(_clock, 189.90m, sku: "CAF-600-PRT", name: "Cafeteira");
        var grinder = CartScenarios.Product(_clock, 49.50m, sku: "MOE-100-PRT", name: "Moedor");
        _products.Seed(coffee);
        _products.Seed(grinder);
        cart.AddItem(coffee.Id, 2, _clock);
        cart.AddItem(grinder.Id, 3, _clock);
        _carts.Seed(cart);

        var result = await GetAsync(cart.Id);

        var view = result.Value;
        view.Items.Count.ShouldBe(2);
        var coffeeLine = view.Items.Single(line => line.ProductId == coffee.Id);
        (coffeeLine.Sku, coffeeLine.Name, coffeeLine.Quantity).ShouldBe(("CAF-600-PRT", "Cafeteira", 2));
        (coffeeLine.UnitPriceAmount, coffeeLine.LineTotalAmount, coffeeLine.Currency).ShouldBe(
            (189.90m, 379.80m, "BRL")
        );
        view.Items.Single(line => line.ProductId == grinder.Id).LineTotalAmount.ShouldBe(148.50m);
        view.SubtotalAmount.ShouldBe(528.30m);
        view.AllowedActions.ShouldBe([CartActions.AddItem, CartActions.RemoveItem, CartActions.Checkout]);
    }

    [Fact]
    public async Task Should_follow_a_price_change_of_the_catalog_without_touching_the_cart()
    {
        var cart = CartScenarios.Cart(_clock, _customer);
        var product = CartScenarios.Product(_clock, 100m);
        _products.Seed(product);
        cart.AddItem(product.Id, 2, _clock);
        _carts.Seed(cart);
        var versionBefore = cart.Version;

        product.ApplyPrice(new Money(120m, "BRL"), 3, _clock);
        var view = (await GetAsync(cart.Id)).Value;

        view.SubtotalAmount.ShouldBe(240m);
        view.Version.ShouldBe(versionBefore);
    }

    [Fact]
    public async Task Should_show_a_line_priced_in_another_currency_but_keep_it_out_of_the_subtotal()
    {
        var cart = CartScenarios.Cart(_clock, _customer, "BRL");
        var inReais = CartScenarios.Product(_clock, 100m, "BRL", sku: "SKU-0001");
        var inDollars = CartScenarios.Product(_clock, 20m, "USD", sku: "SKU-0002");
        _products.Seed(inReais);
        _products.Seed(inDollars);
        cart.AddItem(inReais.Id, 1, _clock);
        cart.AddItem(inDollars.Id, 1, _clock);
        _carts.Seed(cart);

        var view = (await GetAsync(cart.Id)).Value;

        view.Items.Count.ShouldBe(2);
        view.Items.Single(line => line.ProductId == inDollars.Id).Currency.ShouldBe("USD");
        view.SubtotalAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task Should_leave_out_a_line_whose_product_the_cart_cannot_resolve_instead_of_failing()
    {
        var cart = CartScenarios.Cart(_clock, _customer);
        cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        _carts.Seed(cart);

        var view = (await GetAsync(cart.Id)).Value;

        view.Items.ShouldBeEmpty();
        view.SubtotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task Should_answer_not_found_for_a_cart_of_another_customer_exactly_like_a_missing_one()
    {
        var cart = CartScenarios.Cart(_clock, Guid.CreateVersion7());
        _carts.Seed(cart);

        var other = await GetAsync(cart.Id);
        var missing = await GetAsync(Guid.CreateVersion7());

        other.ShouldFail().ShouldBe(missing.ShouldFail());
        other.ShouldFail().Code.ShouldBe("CART_NOT_FOUND");
        other.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_show_a_checked_out_cart_with_no_actions()
    {
        var cart = CartScenarios.Cart(_clock, _customer);
        cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        cart.MarkCheckedOut(_clock);
        _carts.Seed(cart);

        var view = (await GetAsync(cart.Id)).Value;

        view.Status.ShouldBe(CartStatusView.CheckedOut);
        view.AllowedActions.ShouldBeEmpty();
    }
}

public sealed class AddCartItemHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShoppingCartRepository _carts = new();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly AddCartItemHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();
    private readonly ShoppingCart _cart;

    public AddCartItemHandlerTests()
    {
        _handler = new AddCartItemHandler(_carts, _products, _unitOfWork, _clock);
        _cart = CartScenarios.Cart(_clock, _customer);
        _carts.Seed(_cart);
    }

    private Task<Result<CartView>> AddAsync(
        Guid productId,
        int quantity = 1,
        Guid? cartId = null,
        Guid? customer = null
    ) =>
        _handler.HandleAsync(
            new AddCartItemCommand(customer ?? _customer, cartId ?? _cart.Id, productId, quantity),
            TestContext.Current.CancellationToken
        );

    [Fact]
    [Trait("Rule", "BR-CRT-005")]
    public async Task Should_add_a_sellable_product_priced_by_the_catalog_and_return_the_cart()
    {
        var product = CartScenarios.Product(_clock, 189.90m);
        _products.Seed(product);

        var result = await AddAsync(product.Id, 2);

        var view = result.Value;
        var line = view.Items.ShouldHaveSingleItem();
        (line.ProductId, line.Quantity, line.UnitPriceAmount, line.LineTotalAmount).ShouldBe(
            (product.Id, 2, 189.90m, 379.80m)
        );
        view.SubtotalAmount.ShouldBe(379.80m);
        view.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        _carts.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-003")]
    public async Task Should_increase_the_line_when_the_same_product_is_added_again()
    {
        var product = CartScenarios.Product(_clock);
        _products.Seed(product);
        await AddAsync(product.Id, 2);

        var result = await AddAsync(product.Id, 3);

        result.Value.Items.ShouldHaveSingleItem().Quantity.ShouldBe(5);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-005")]
    public async Task Should_refuse_a_product_the_cart_does_not_know()
    {
        var result = await AddAsync(Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_PRODUCT_NOT_AVAILABLE");
        _cart.Items.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-005")]
    public async Task Should_refuse_a_product_that_is_not_sellable()
    {
        var draft = CartScenarios.Product(_clock, sellable: false);
        _products.Seed(draft);

        var result = await AddAsync(draft.Id);

        result.ShouldFail().Code.ShouldBe("CART_PRODUCT_NOT_AVAILABLE");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-005")]
    public async Task Should_refuse_a_product_priced_in_another_currency()
    {
        var dollars = CartScenarios.Product(_clock, currency: "USD");
        _products.Seed(dollars);

        var result = await AddAsync(dollars.Id);

        result.ShouldFail().Code.ShouldBe("CART_CURRENCY_MISMATCH");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [Trait("Rule", "BR-CRT-002")]
    public async Task Should_reject_a_quantity_outside_the_range_and_persist_nothing(int quantity)
    {
        var product = CartScenarios.Product(_clock);
        _products.Seed(product);

        var result = await AddAsync(product.Id, quantity);

        result.ShouldFail().Code.ShouldBe("CART_ITEM_QUANTITY_INVALID");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_answer_not_found_for_a_cart_of_another_customer_before_looking_at_the_product()
    {
        var result = await AddAsync(Guid.CreateVersion7(), customer: Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_answer_not_found_for_a_cart_that_does_not_exist()
    {
        var result = await AddAsync(Guid.CreateVersion7(), cartId: Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_NOT_FOUND");
    }

    [Fact]
    [Trait("Rule", "BR-CRT-004")]
    public async Task Should_answer_a_conflict_for_a_cart_that_is_no_longer_active_before_validating_the_product()
    {
        _cart.AddItem(Guid.CreateVersion7(), 1, _clock);
        _cart.MarkCheckedOut(_clock);

        var result = await AddAsync(Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_NOT_ACTIVE");
        result.ShouldFail().Type.ShouldBe(ErrorType.Conflict);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-003")]
    public async Task Should_refuse_a_new_line_beyond_the_maximum_number_of_lines()
    {
        for (var index = 0; index < ShoppingCart.MaxDistinctItems; index++)
        {
            var existing = CartScenarios.Product(_clock, sku: $"SKU-{index:0000}");
            _products.Seed(existing);
            _cart.AddItem(existing.Id, 1, _clock);
        }

        var extra = CartScenarios.Product(_clock, sku: "SKU-9999");
        _products.Seed(extra);

        var result = await AddAsync(extra.Id);

        result.ShouldFail().Code.ShouldBe("CART_ITEM_LIMIT");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}

public sealed class RemoveCartItemHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeShoppingCartRepository _carts = new();
    private readonly FakeCatalogProductRepository _products = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RemoveCartItemHandler _handler;
    private readonly Guid _customer = Guid.CreateVersion7();
    private readonly ShoppingCart _cart;

    public RemoveCartItemHandlerTests()
    {
        _handler = new RemoveCartItemHandler(_carts, _products, _unitOfWork, _clock);
        _cart = CartScenarios.Cart(_clock, _customer);
        _carts.Seed(_cart);
    }

    private Task<Result<CartView>> RemoveAsync(Guid itemId, Guid? customer = null, Guid? cartId = null) =>
        _handler.HandleAsync(
            new RemoveCartItemCommand(customer ?? _customer, cartId ?? _cart.Id, itemId),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Should_remove_the_line_and_return_the_remaining_cart_with_its_subtotal()
    {
        var kept = CartScenarios.Product(_clock, 100m, sku: "SKU-0001");
        var removed = CartScenarios.Product(_clock, 50m, sku: "SKU-0002");
        _products.Seed(kept);
        _products.Seed(removed);
        _cart.AddItem(kept.Id, 1, _clock);
        var line = _cart.AddItem(removed.Id, 4, _clock).Value;

        var result = await RemoveAsync(line.Id);

        var view = result.Value;
        view.Items.ShouldHaveSingleItem().ProductId.ShouldBe(kept.Id);
        view.SubtotalAmount.ShouldBe(100m);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_report_a_missing_line_as_not_found_and_persist_nothing()
    {
        var result = await RemoveAsync(Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_ITEM_NOT_FOUND");
        result.ShouldFail().Type.ShouldBe(ErrorType.NotFound);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_answer_not_found_for_a_cart_of_another_customer()
    {
        var line = _cart.AddItem(Guid.CreateVersion7(), 1, _clock).Value;

        var result = await RemoveAsync(line.Id, customer: Guid.CreateVersion7());

        result.ShouldFail().Code.ShouldBe("CART_NOT_FOUND");
        _cart.Items.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("Rule", "BR-CRT-004")]
    public async Task Should_answer_a_conflict_for_a_cart_that_is_no_longer_active()
    {
        var line = _cart.AddItem(Guid.CreateVersion7(), 1, _clock).Value;
        _cart.MarkCheckedOut(_clock);

        var result = await RemoveAsync(line.Id);

        result.ShouldFail().Code.ShouldBe("CART_NOT_ACTIVE");
        _cart.Items.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
