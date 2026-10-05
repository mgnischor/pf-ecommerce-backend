using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Cart.Domain;

namespace Portfolio.Cart.Infrastructure;

/// <summary>Mapping of <c>cart.carts</c> (BR-CRT-001, BR-CRT-004). Shared columns come from the entity conventions.</summary>
internal sealed class ShoppingCartConfiguration : IEntityTypeConfiguration<ShoppingCart>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShoppingCart> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var statuses = string.Join(", ", Enum.GetNames<CartStatus>().Select(name => $"'{name}'"));

        builder.ToTable("carts", table => table.HasCheckConstraint("ck_carts_status", $"status IN ({statuses})"));

        builder.Property(cart => cart.CustomerId).IsRequired();
        builder.Property(cart => cart.Currency).HasColumnType("char(3)").IsFixedLength().IsRequired();
        builder.Property(cart => cart.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // BR-CRT-001: one active cart per customer. The index is partial, so a cart that was checked out or expired,
        // or logically deleted, does not keep the customer from opening another.
        builder
            .HasIndex(cart => cart.CustomerId)
            .IsUnique()
            .HasFilter("status = 'Active' AND deleted_at IS NULL")
            .HasDatabaseName("ux_carts_customer_active");

        // Removing a line is a logical deletion of the line (the auditing interceptor), never of the cart.
        builder
            .HasMany(cart => cart.Items)
            .WithOne()
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(cart => cart.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
