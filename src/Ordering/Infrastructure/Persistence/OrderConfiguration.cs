using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Ordering.Domain;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>Mapping of <c>ordering.orders</c> (BR-ORD-001 to BR-ORD-005). Shared columns come from the entity conventions.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var statuses = string.Join(", ", Enum.GetNames<OrderStatus>().Select(name => $"'{name}'"));

        builder.ToTable(
            "orders",
            table =>
            {
                table.HasCheckConstraint("ck_orders_status", $"status IN ({statuses})");
                table.HasCheckConstraint("ck_orders_total_positive", "total_amount > 0");
                table.HasCheckConstraint(
                    "ck_orders_cancellation",
                    "(status = 'Cancelled') = (cancelled_at IS NOT NULL AND cancellation_reason IS NOT NULL)"
                );
            }
        );

        builder.Property(order => order.Number).HasMaxLength(Order.MaxNumberLength).IsRequired();
        builder.Property(order => order.CheckoutId).IsRequired();
        builder.Property(order => order.CustomerId).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.PlacedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(order => order.PaidAt).HasColumnType("timestamptz");
        builder.Property(order => order.ShippedAt).HasColumnType("timestamptz");
        builder.Property(order => order.DeliveredAt).HasColumnType("timestamptz");
        builder.Property(order => order.CancelledAt).HasColumnType("timestamptz");
        builder.Property(order => order.CancellationReason).HasMaxLength(Order.MaxReasonLength);
        builder.Property(order => order.CancellationNote).HasMaxLength(Order.MaxNoteLength);
        builder.Property(order => order.CancellationKey).HasMaxLength(Order.MaxIdempotencyKeyLength);

        // Money is a value object stored as two columns: amount numeric(19,4) and ISO 4217 currency char(3).
        builder.ComplexProperty(
            order => order.Total,
            total =>
            {
                total.Property(money => money.Amount).HasColumnName("total_amount").HasColumnType("numeric(19,4)");
                total
                    .Property(money => money.Currency)
                    .HasColumnName("total_currency")
                    .HasColumnType("char(3)")
                    .IsFixedLength();
            }
        );

        ConfigureIndexes(builder);

        builder
            .HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Order> builder)
    {
        // BR-ORD-005: an order number is never reused, so the index covers logically deleted orders too.
        builder.HasIndex(order => order.Number).IsUnique().HasDatabaseName("ux_orders_number");

        // BR-ORD-001: a checkout produces at most one order. This closes the race between two concurrent placements.
        builder.HasIndex(order => order.CheckoutId).IsUnique().HasDatabaseName("ux_orders_checkout");

        // BR-ORD-006: a customer lists their own orders, newest first or by total, keyset-paged on the identifier.
        builder
            .HasIndex(order => new
            {
                order.CustomerId,
                order.PlacedAt,
                order.Id,
            })
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_orders_customer_placed_at");
    }
}
