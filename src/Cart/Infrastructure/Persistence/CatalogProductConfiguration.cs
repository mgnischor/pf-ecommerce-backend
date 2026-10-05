using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>Mapping of <c>cart.catalog_products</c>, the Cart's own view of the catalog (BR-CRT-005).</summary>
internal sealed class CatalogProductConfiguration : IEntityTypeConfiguration<CatalogProduct>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CatalogProduct> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "catalog_products",
            table =>
            {
                table.HasCheckConstraint("ck_catalog_products_price_positive", "price_amount > 0");
                table.HasCheckConstraint(
                    "ck_catalog_products_source_versions",
                    "price_version >= 1 AND status_version >= 1"
                );
            }
        );

        builder.Property(product => product.Sku).HasMaxLength(32).IsRequired();
        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.Sellable).IsRequired();
        builder.Property(product => product.PriceVersion).IsRequired();
        builder.Property(product => product.StatusVersion).IsRequired();

        // Money is a value object stored as two columns: amount numeric(19,4) and ISO 4217 currency char(3).
        builder.ComplexProperty(
            product => product.Price,
            price =>
            {
                price.Property(money => money.Amount).HasColumnName("price_amount").HasColumnType("numeric(19,4)");
                price
                    .Property(money => money.Currency)
                    .HasColumnName("price_currency")
                    .HasColumnType("char(3)")
                    .IsFixedLength();
            }
        );
    }
}
