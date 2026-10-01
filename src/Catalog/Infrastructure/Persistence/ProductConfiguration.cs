using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Catalog.Domain;

namespace Portfolio.Catalog.Infrastructure;

/// <summary>Mapping of <c>catalog.products</c> (BR-CAT-001 to BR-CAT-005). Shared columns come from the entity conventions.</summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var statuses = string.Join(", ", Enum.GetNames<ProductStatus>().Select(name => $"'{name}'"));

        builder.ToTable(
            "products",
            table =>
            {
                table.HasCheckConstraint("ck_products_price_positive", "price_amount > 0");
                table.HasCheckConstraint("ck_products_status", $"status IN ({statuses})");
                table.HasCheckConstraint(
                    "ck_products_name_length",
                    $"char_length(name) BETWEEN {Product.MinNameLength} AND {Product.MaxNameLength}"
                );
            }
        );

        builder.Property(product => product.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(Product.MaxDescriptionLength);
        builder
            .Property(product => product.Sku)
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .IsRequired();
        builder.Property(product => product.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

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

        // BR-CAT-005: one active product per SKU; a logically deleted product releases its SKU.
        builder
            .HasIndex(product => product.Sku)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_products_sku_active");

        // Catalog listing by status, newest first.
        builder
            .HasIndex(product => new { product.Status, product.CreatedAt })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_products_status_created_at");
    }
}
