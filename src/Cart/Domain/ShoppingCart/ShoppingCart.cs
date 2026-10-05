using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// The shopper's pre-purchase selection (aggregate root). Enforces on every change: BR-CRT-001 (one active cart per
/// customer, checked by the Application layer and a partial unique index), BR-CRT-002 (line quantity), BR-CRT-003
/// (number of lines), BR-CRT-004 (lifecycle Active → CheckedOut | Expired).
/// Lines refer to products by identifier only; prices are never stored here (BR-CRT-005).
/// </summary>
internal sealed class ShoppingCart : AggregateRoot
{
    /// <summary>Largest quantity of one product in the cart, and of one request (BR-CRT-002).</summary>
    public const int MaxLineQuantity = 99;

    /// <summary>Largest number of distinct products in the cart (BR-CRT-003).</summary>
    public const int MaxDistinctItems = 50;

    private readonly List<ShoppingCartItem> _items = [];

    /// <summary>The customer (account) that owns the cart.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>ISO 4217 currency the cart is priced in. Fixed at creation.</summary>
    public string Currency { get; private set; }

    /// <summary>Lifecycle status.</summary>
    public CartStatus Status { get; private set; }

    /// <summary>The lines of the cart.</summary>
    public IReadOnlyCollection<ShoppingCartItem> Items => _items.AsReadOnly();

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private ShoppingCart() { }
#pragma warning restore CS8618

    private ShoppingCart(Guid id, Guid customerId, string currency, TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        CustomerId = customerId;
        Currency = currency;
        Status = CartStatus.Active;
    }

    /// <summary>Opens an empty, active cart (BR-CRT-001).</summary>
    /// <param name="customerId">Owner.</param>
    /// <param name="currency">ISO 4217 currency the cart is priced in.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static Result<ShoppingCart> Open(Guid customerId, string? currency, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);

        var money = Money.Create(0m, currency);
        return money.IsFailure
            ? Result<ShoppingCart>.Failure(money.Error)
            : Result<ShoppingCart>.Success(
                new ShoppingCart(NewId(timeProvider), customerId, money.Value.Currency, timeProvider)
            );
    }

    /// <summary>
    /// Adds units of a product, or increases the line of a product already in the cart (BR-CRT-002, BR-CRT-003).
    /// Whether the product is sellable and priced in the cart's currency is checked by the caller (BR-CRT-005).
    /// </summary>
    /// <param name="productId">Product.</param>
    /// <param name="quantity">Units to add, 1 to <see cref="MaxLineQuantity"/>.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result<ShoppingCartItem> AddItem(Guid productId, int quantity, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return Result<ShoppingCartItem>.Failure(active.Error);
        }

        if (quantity is < 1 or > MaxLineQuantity)
        {
            return Result<ShoppingCartItem>.Failure(CartErrors.QuantityInvalid);
        }

        var line = _items.Find(item => item.ProductId == productId);
        if (line is not null)
        {
            if (line.Quantity + quantity > MaxLineQuantity)
            {
                return Result<ShoppingCartItem>.Failure(CartErrors.LineQuantityLimit);
            }

            line.Increase(quantity, timeProvider);
        }
        else
        {
            if (_items.Count >= MaxDistinctItems)
            {
                return Result<ShoppingCartItem>.Failure(CartErrors.ItemLimit);
            }

            line = ShoppingCartItem.Create(Id, productId, quantity, timeProvider);
            _items.Add(line);
        }

        MarkUpdated(timeProvider);
        return Result<ShoppingCartItem>.Success(line);
    }

    /// <summary>Removes a line from the cart.</summary>
    /// <param name="itemId">Line identifier.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result RemoveItem(Guid itemId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active;
        }

        var line = _items.Find(item => item.Id == itemId);
        if (line is null)
        {
            return Result.Failure(CartErrors.ItemNotFound);
        }

        _items.Remove(line);
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    /// <summary>Converts the cart into a checkout (BR-CRT-004). An empty cart cannot be checked out.</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result MarkCheckedOut(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status != CartStatus.Active)
        {
            return Result.Failure(CartErrors.InvalidStatusTransition(Status, CartStatus.CheckedOut));
        }

        if (_items.Count == 0)
        {
            return Result.Failure(CartErrors.EmptyCart);
        }

        Status = CartStatus.CheckedOut;
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    /// <summary>Expires the cart after inactivity (BR-CRT-004).</summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Expire(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (Status != CartStatus.Active)
        {
            return Result.Failure(CartErrors.InvalidStatusTransition(Status, CartStatus.Expired));
        }

        Status = CartStatus.Expired;
        MarkUpdated(timeProvider);
        return Result.Success();
    }

    /// <summary>Whether the cart can still change (BR-CRT-004).</summary>
    public Result EnsureActive() =>
        Status == CartStatus.Active ? Result.Success() : Result.Failure(CartErrors.NotActive(Status));
}
