using Portfolio.Catalog.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.UnitTests.Catalog.Domain;

/// <summary>
/// Builds valid products with sensible defaults; tests override only what they assert on (ai/TESTS.md §10).
/// The returned product has no pending domain events, so assertions see only what the test provokes.
/// </summary>
internal sealed class ProductBuilder
{
    private string _name = "Cafeteira Elétrica 600ml";
    private string _sku = "CAF-600-PRT";
    private Money _price = new(189.90m, "BRL");
    private string? _description = "Cafeteira elétrica com filtro permanente.";
    private ProductStatus _status = ProductStatus.Draft;

    public static ProductBuilder New() => new();

    public ProductBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ProductBuilder WithSku(string sku)
    {
        _sku = sku;
        return this;
    }

    public ProductBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    public ProductBuilder WithPrice(decimal amount, string currency = "BRL")
    {
        _price = new Money(amount, currency);
        return this;
    }

    public ProductBuilder WithStatus(ProductStatus status)
    {
        _status = status;
        return this;
    }

    public Product Build(TimeProvider timeProvider)
    {
        var sku = Sku.Create(_sku).Value;
        var product = Product.Create(_name, sku, _price, _description, timeProvider).Value;

        if (_status is ProductStatus.Active or ProductStatus.Discontinued)
        {
            product.Activate(timeProvider);
        }

        if (_status is ProductStatus.Discontinued)
        {
            product.Discontinue(timeProvider);
        }

        product.ClearDomainEvents();
        return product;
    }
}
