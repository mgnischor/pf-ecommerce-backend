using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Inventory.Domain;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>Mapping of <c>inventory.inventory_items</c> (BR-INV-001, BR-INV-008). Shared columns come from the entity conventions.</summary>
internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "inventory_items",
            table =>
            {
                // BR-INV-001: the safety net under the domain rule, in the database (ai/DATABASE.md §3.3).
                table.HasCheckConstraint(
                    "ck_inventory_items_stock_bounds",
                    $"reserved >= 0 AND on_hand >= reserved AND on_hand <= {InventoryItem.MaxOnHand}"
                );
            }
        );

        builder
            .Property(item => item.Sku)
            .HasMaxLength(Sku.MaxLength)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .IsRequired();
        builder.Property(item => item.OnHand).IsRequired();
        builder.Property(item => item.Reserved).IsRequired();
        builder.Ignore(item => item.Available);

        // BR-INV-008: one active item per SKU; a logically deleted item releases its SKU.
        builder
            .HasIndex(item => item.Sku)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_inventory_items_sku_active");

        builder
            .HasMany(item => item.Movements)
            .WithOne()
            .HasForeignKey(movement => movement.InventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(item => item.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
