using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// The Cart's own view of a catalog product: just what the cart needs to show and to accept a product (SKU, name, price,
/// whether it can be bought). It is kept up to date from the Catalog's integration events and is never written through
/// the API, so the Cart never calls into, or shares types or tables with, the Catalog (anti-corruption layer,
/// ai/ARCHITECTURE.md §2.1, BR-CRT-005).
/// </summary>
/// <remarks>
/// Events arrive at least once and possibly out of order, so each facet remembers the Catalog version of the event that
/// last set it: the price only moves forward through <see cref="PriceVersion"/>, and the sellable flag through
/// <see cref="StatusVersion"/> (status changes and deletion share the second). An older event is ignored.
/// </remarks>
internal sealed class CatalogProduct : AggregateRoot
{
    /// <summary>Stock-keeping unit.</summary>
    public string Sku { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; private set; }

    /// <summary>Current sell price.</summary>
    public Money Price { get; private set; }

    /// <summary>Whether the product can be added to a cart: active and not deleted.</summary>
    public bool Sellable { get; private set; }

    /// <summary>Catalog version of the event that last set <see cref="Price"/>.</summary>
    public int PriceVersion { get; private set; }

    /// <summary>Catalog version of the event that last set <see cref="Sellable"/>.</summary>
    public int StatusVersion { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private CatalogProduct() { }
#pragma warning restore CS8618

    private CatalogProduct(
        Guid productId,
        string sku,
        string name,
        Money price,
        int sourceVersion,
        TimeProvider timeProvider
    )
        : base(productId, timeProvider)
    {
        Sku = sku;
        Name = name;
        Price = price;
        Sellable = false;
        PriceVersion = sourceVersion;
        StatusVersion = sourceVersion;
    }

    /// <summary>Starts the view from the Catalog's <c>ProductCreated</c> event. A new product is a draft, so not sellable.</summary>
    /// <param name="productId">The catalog product identifier, which this view reuses.</param>
    /// <param name="sku">SKU.</param>
    /// <param name="name">Name.</param>
    /// <param name="price">Initial price.</param>
    /// <param name="sourceVersion">Catalog version of the event.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static CatalogProduct Register(
        Guid productId,
        string sku,
        string name,
        Money price,
        int sourceVersion,
        TimeProvider timeProvider
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new CatalogProduct(productId, sku, name, price, sourceVersion, timeProvider);
    }

    /// <summary>Applies a price change from the Catalog.</summary>
    /// <param name="newPrice">The new price.</param>
    /// <param name="sourceVersion">Catalog version of the event.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns><c>false</c> when the event was older than what the view already holds and was ignored.</returns>
    public bool ApplyPrice(Money newPrice, int sourceVersion, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (sourceVersion <= PriceVersion)
        {
            return false;
        }

        Price = newPrice;
        PriceVersion = sourceVersion;
        MarkUpdated(timeProvider);
        return true;
    }

    /// <summary>Applies a lifecycle change from the Catalog.</summary>
    /// <param name="sellable">Whether the product is now active.</param>
    /// <param name="sourceVersion">Catalog version of the event.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns><c>false</c> when the event was older than what the view already holds and was ignored.</returns>
    public bool ApplyStatus(bool sellable, int sourceVersion, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (sourceVersion <= StatusVersion)
        {
            return false;
        }

        Sellable = sellable;
        StatusVersion = sourceVersion;
        MarkUpdated(timeProvider);
        return true;
    }

    /// <summary>Applies the Catalog's deletion of the product: it is no longer sellable.</summary>
    /// <param name="sourceVersion">Catalog version of the event.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns><c>false</c> when the event was older than what the view already holds and was ignored.</returns>
    public bool ApplyDeletion(int sourceVersion, TimeProvider timeProvider) =>
        ApplyStatus(sellable: false, sourceVersion, timeProvider);

    /// <summary>
    /// Whether the product can be added to a cart priced in <paramref name="cartCurrency"/> (BR-CRT-005).
    /// </summary>
    /// <param name="cartCurrency">ISO 4217 currency of the cart.</param>
    public Result EnsureBuyableIn(string cartCurrency)
    {
        ArgumentException.ThrowIfNullOrEmpty(cartCurrency);

        if (!Sellable)
        {
            return Result.Failure(CartErrors.ProductNotAvailable);
        }

        return string.Equals(Price.Currency, cartCurrency, StringComparison.Ordinal)
            ? Result.Success()
            : Result.Failure(CartErrors.CurrencyMismatch(cartCurrency));
    }
}
