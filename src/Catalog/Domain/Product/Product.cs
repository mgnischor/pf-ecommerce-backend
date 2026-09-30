using Portfolio.SharedKernel.Domain;

namespace Portfolio.Catalog.Domain;

/// <summary>
/// Catalog product aggregate root.
/// Enforces its invariants on every state change:
/// BR-CAT-001 (naming and description), BR-CAT-002 (positive price), BR-CAT-003 (lifecycle Draft → Active → Discontinued).
/// SKU format (BR-CAT-004) is enforced by <see cref="Sku"/>; SKU uniqueness (BR-CAT-005) needs the repository
/// and is enforced by the Application layer plus a partial unique index.
/// Cross-aggregate references use identifiers only; the price snapshot for orders
/// is taken from <see cref="Price"/> at purchase time.
/// </summary>
internal sealed class Product : AggregateRoot
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
    private Product() { }
#pragma warning restore CS8618

    private Product(Guid id, string name, Sku sku, Money price, string? description, TimeProvider timeProvider)
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
    /// <param name="description">Optional description (max 2000 characters). Blank is stored as <c>null</c>.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns>A successful result with the product, or a failure describing the violated rule.</returns>
    public static Result<Product> Create(
        string? name,
        Sku sku,
        Money price,
        string? description,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (ValidateName(name, out var normalizedName) is { } nameError)
        {
            return Result<Product>.Failure(nameError);
        }

        if (ValidateDescription(description, out var normalizedDescription) is { } descriptionError)
        {
            return Result<Product>.Failure(descriptionError);
        }

        if (ValidatePrice(price) is { } priceError)
        {
            return Result<Product>.Failure(priceError);
        }

        var product = new Product(NewId(timeProvider), normalizedName, sku, price, normalizedDescription, timeProvider);
        product.AddDomainEvent(ProductCreated.For(product));

        return Result<Product>.Success(product);
    }

    /// <summary>
    /// Renames the product (BR-CAT-001).
    /// </summary>
    /// <param name="name">New display name (3–200 characters).</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result Rename(string? name, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = ValidateName(name, out var normalizedName);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Name = normalizedName;
        MarkUpdated(timeProvider);

        return Result.Success();
    }

    /// <summary>
    /// Changes the description (BR-CAT-001).
    /// </summary>
    /// <param name="description">New description (max 2000 characters). Blank clears it.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public Result ChangeDescription(string? description, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var error = ValidateDescription(description, out var normalizedDescription);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Description = normalizedDescription;
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
        AddDomainEvent(ProductPriceChanged.For(this, oldPrice));

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
            return Result.Failure(ProductErrors.InvalidStatusTransition(Status, status));
        }

        var from = Status;
        Status = status;
        MarkUpdated(timeProvider);
        AddDomainEvent(ProductStatusChanged.For(this, from));

        return Result.Success();
    }

    private static bool IsValidTransition(ProductStatus from, ProductStatus to) =>
        (from, to) switch
        {
            (ProductStatus.Draft, ProductStatus.Active) => true,
            (ProductStatus.Active, ProductStatus.Discontinued) => true,
            _ => false,
        };

    private static Error? ValidateName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;

        if (normalized.Length == 0)
        {
            return ProductErrors.NameRequired;
        }

        return normalized.Length is < MinNameLength or > MaxNameLength ? ProductErrors.NameLength : null;
    }

    private static Error? ValidateDescription(string? description, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        return normalized is { Length: > MaxDescriptionLength } ? ProductErrors.DescriptionTooLong : null;
    }

    private static Error? ValidatePrice(Money price) => price.IsPositive ? null : ProductErrors.PriceMustBePositive;
}
