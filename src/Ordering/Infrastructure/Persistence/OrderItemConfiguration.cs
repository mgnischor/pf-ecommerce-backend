using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Ordering.Domain;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>Mapping of <c>ordering.order_items</c>, the price snapshot of an order (BR-ORD-002).</summary>
internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "order_items",
            table =>
            {
                table.HasCheckConstraint("ck_order_items_quantity", $"quantity BETWEEN 1 AND {Order.MaxLineQuantity}");
                table.HasCheckConstraint("ck_order_items_unit_price_positive", "unit_price_amount > 0");
            }
        );

        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.Sku).HasMaxLength(OrderItem.MaxSkuLength).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(OrderItem.MaxNameLength).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Ignore(item => item.LineTotal);

        builder.ComplexProperty(
            item => item.UnitPrice,
            price =>
            {
                price.Property(money => money.Amount).HasColumnName("unit_price_amount").HasColumnType("numeric(19,4)");
                price
                    .Property(money => money.Currency)
                    .HasColumnName("unit_price_currency")
                    .HasColumnType("char(3)")
                    .IsFixedLength();
            }
        );

        // Reading the lines of an order.
        builder.HasIndex(item => item.OrderId).HasDatabaseName("ix_order_items_order_id");
    }
}
