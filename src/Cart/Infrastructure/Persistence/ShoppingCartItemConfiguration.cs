using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>Mapping of <c>cart.cart_items</c> (BR-CRT-002, BR-CRT-003). The line stores no price.</summary>
internal sealed class ShoppingCartItemConfiguration : IEntityTypeConfiguration<ShoppingCartItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShoppingCartItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "cart_items",
            table =>
                table.HasCheckConstraint(
                    "ck_cart_items_quantity",
                    $"quantity BETWEEN 1 AND {ShoppingCart.MaxLineQuantity}"
                )
        );

        builder.Property(item => item.CartId).IsRequired();
        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();

        // BR-CRT-003: one line per product in a cart; a removed line (logically deleted) lets the product be added again.
        builder
            .HasIndex(item => new { item.CartId, item.ProductId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_cart_items_cart_product");
    }
}
