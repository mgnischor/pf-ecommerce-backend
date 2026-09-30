using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Catalog product aggregate root.
/// Enforces its invariants on every state change:
/// BR-CAT-001 (naming), BR-CAT-002 (positive price), BR-CAT-003 (lifecycle Draft → Active → Discontinued).
/// Cross-aggregate references use identifiers only; the price snapshot for orders
/// is taken from <see cref="Price"/> at purchase time.
/// </summary>
public sealed class Product : AggregateRoot
{
    /// <summary>Minimum length of a product name (BR-CAT-001).</summary>
    public const int MinNameLength = 3;

    /// <summary>Maximum length of a product name (BR-CAT-001).</summary>
    public const int MaxNameLength = 200;

    /// <summary>Maximum length of a product description (BR-CAT-001).</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>Product display name.</summary>
    public string Name { get; private set; }

    /// <summary>Optional product description.</summary>
    public string? Description { get; private set; }

    /// <summary>Stock-keeping unit. Unique within the catalog.</summary>
    public Sku Sku { get; private set; }

    /// <summary>Current sell price.</summary>
    public Money Price { get; private set; }

    /// <summary>Lifecycle status.</summary>
    public ProductStatus Status { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private Product()
    {
    }
#pragma warning restore CS8618

    private Product(
        Guid id,
        string name,
        Sku sku,
        Money price,
        string? description,
        TimeProvider timeProvider)
        : base(id, timeProvider)
    {
        Name = name;
        Sku = sku;
        Price = price;
        Description = description;
        Status = ProductStatus.Draft;
    }

    /// <summary>
    /// Creates a new product in <see cref="ProductStatus.Draft"/> status.
    /// </summary>
    /// <param name="name">Display name (3–200 characters).</param>
    /// <param name="sku">Stock-keeping unit.</param>
    /// <param name="price">Sell price. Amount must be positive.</param>
    /// <param name="description">Optional description (max 2000 characters).</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns>A successful result with the product, or a failure describing the violated rule.</returns>
    public static Result<Product> Create(
        string name,
        Sku sku,
        Money price,
        string? description,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var nameError = ValidateName(name);
        if (nameError is not null)
        {
            return Result<Product>.Failure(nameError);
        }

        var descriptionError = ValidateDescription(description);
        if (descriptionError is not null)
        {
            return Result<Product>.Failure(descriptionError);
        }

        var priceError = ValidatePrice(price);
        if (priceError is not null)
        {
            return Result<Product>.Failure(priceError);
        }

        var product = new Product(NewId(), name.Trim(), sku, price, description?.Trim(), timeProvider);
        product.AddDomainEvent(ProductCreated.For(product, timeProvider));

        return Result<Product>.Success(product);
    }

    /// <summary>
    /// Renames the product (BR-CAT-001).
    /// </summary>
    /// <param name="name">New display name (3–200 characters).</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Rename(string name, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = ValidateName(name);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Name = name.Trim();
        MarkUpdated(timeProvider);

        return Result.Success();
    }

    /// <summary>
    /// Changes the sell price (BR-CAT-002). Emits <see cref="ProductPriceChanged"/> when the value differs.
    /// </summary>
    /// <param name="price">New sell price. Amount must be positive.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result ChangePrice(Money price, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = ValidatePrice(price);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        if (Price.Equals(price))
        {
            return Result.Success();
        }

        var oldPrice = Price;
        Price = price;
        MarkUpdated(timeProvider);
        AddDomainEvent(ProductPriceChanged.For(this, oldPrice, timeProvider));

        return Result.Success();
    }

    /// <summary>
    /// Makes a draft product sellable (BR-CAT-003).
    /// </summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Activate(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TransitionTo(ProductStatus.Active, timeProvider);
    }

    /// <summary>
    /// Permanently withdraws an active product from the catalog (BR-CAT-003).
    /// </summary>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Discontinue(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TransitionTo(ProductStatus.Discontinued, timeProvider);
    }

    private Result TransitionTo(ProductStatus status, TimeProvider timeProvider)
    {
        if (!IsValidTransition(Status, status))
        {
            return Result.Failure(Error.Conflict(
                "PRODUCT_INVALID_STATUS_TRANSITION",
                $"Cannot transition a product from '{Status}' to '{status}'.",
                "BR-CAT-003"));
        }

        var from = Status;
        Status = status;
        MarkUpdated(timeProvider);
        AddDomainEvent(ProductStatusChanged.For(this, from, timeProvider));

        return Result.Success();
    }

    private static bool IsValidTransition(ProductStatus from, ProductStatus to) =>
        (from, to) switch
        {
            (ProductStatus.Draft, ProductStatus.Active) => true,
            (ProductStatus.Active, ProductStatus.Discontinued) => true,
            _ => false,
        };

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(
                "PRODUCT_NAME_REQUIRED", "Product name is required.", "name", "BR-CAT-001");
        }

        if (name.Trim().Length is < MinNameLength or > MaxNameLength)
        {
            return Error.Validation(
                "PRODUCT_NAME_LENGTH",
                $"Product name must be between {MinNameLength} and {MaxNameLength} characters.",
                "name",
                "BR-CAT-001");
        }

        return null;
    }

    private static Error? ValidateDescription(string? description)
    {
        if (description is not null && description.Trim().Length > MaxDescriptionLength)
        {
            return Error.Validation(
                "PRODUCT_DESCRIPTION_TOO_LONG",
                $"Product description must not exceed {MaxDescriptionLength} characters.",
                "description",
                "BR-CAT-001");
        }

        return null;
    }

    private static Error? ValidatePrice(Money price)
    {
        if (price.Amount <= 0)
        {
            return Error.Validation(
                "PRODUCT_PRICE_MUST_BE_POSITIVE", "Product price must be greater than zero.", "price", "BR-CAT-002");
        }

        return null;
    }
}
