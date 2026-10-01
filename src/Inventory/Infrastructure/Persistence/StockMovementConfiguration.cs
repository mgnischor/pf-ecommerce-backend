using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Inventory.Domain;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>Mapping of <c>inventory.stock_movements</c>, the append-only ledger (BR-INV-003).</summary>
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    /// <summary>Longest idempotency key the API accepts (<c>ApiHeaders.IdempotencyKeyMaxLength</c>).</summary>
    private const int MaxIdempotencyKeyLength = 64;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "stock_movements",
            table =>
            {
                table.HasCheckConstraint("ck_stock_movements_delta", "delta <> 0");
                table.HasCheckConstraint("ck_stock_movements_on_hand_after", "on_hand_after >= 0");
            }
        );

        builder.Property(movement => movement.InventoryItemId).IsRequired();
        builder.Property(movement => movement.Delta).IsRequired();
        builder.Property(movement => movement.ReasonCode).HasMaxLength(StockMovement.MaxReasonLength).IsRequired();
        builder.Property(movement => movement.OnHandAfter).IsRequired();
        builder.Property(movement => movement.RecordedBy).IsRequired();
        builder.Property(movement => movement.IdempotencyKey).HasMaxLength(MaxIdempotencyKeyLength);

        // BR-INV-003: a key can record one movement per item, which closes the double-submit race.
        builder
            .HasIndex(movement => new { movement.InventoryItemId, movement.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_stock_movements_item_idempotency_key");

        // Reading an item's history, newest first.
        builder
            .HasIndex(movement => new { movement.InventoryItemId, movement.CreatedAt })
            .HasDatabaseName("ix_stock_movements_item_created_at");
    }
}
