using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>Mapping of <c>shipping.shipments</c> (BR-SHP-001 to BR-SHP-003). Shared columns come from the entity conventions.</summary>
internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var statuses = string.Join(", ", Enum.GetNames<ShipmentStatus>().Select(name => $"'{name}'"));

        builder.ToTable(
            "shipments",
            table =>
            {
                table.HasCheckConstraint("ck_shipments_status", $"status IN ({statuses})");

                // A shipment has been with the carrier exactly when it is in transit or ended after that.
                table.HasCheckConstraint(
                    "ck_shipments_dispatch",
                    "(dispatched_at IS NOT NULL) = (status IN ('InTransit', 'Delivered', 'Failed'))"
                );
                table.HasCheckConstraint(
                    "ck_shipments_carrier",
                    "(status IN ('Preparing', 'Cancelled')) = (carrier IS NULL)"
                );
            }
        );

        builder.Property(shipment => shipment.OrderId).IsRequired();
        builder.Property(shipment => shipment.CustomerId).IsRequired();
        builder.Property(shipment => shipment.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(shipment => shipment.Carrier).HasMaxLength(Shipment.MaxCarrierLength);
        builder.Property(shipment => shipment.TrackingCode).HasMaxLength(Shipment.MaxTrackingCodeLength);
        builder.Property(shipment => shipment.EstimatedDeliveryDate).HasColumnType("date");
        builder.Property(shipment => shipment.DispatchedAt).HasColumnType("timestamptz");
        builder.Property(shipment => shipment.DeliveredAt).HasColumnType("timestamptz");
        builder.Property(shipment => shipment.FailedAt).HasColumnType("timestamptz");
        builder.Property(shipment => shipment.FailureReason).HasMaxLength(Shipment.MaxReasonLength);
        builder.Property(shipment => shipment.CancelledAt).HasColumnType("timestamptz");

        // BR-SHP-001: one shipment per order. This closes the race between two deliveries of the paid event.
        builder.HasIndex(shipment => shipment.OrderId).IsUnique().HasDatabaseName("ux_shipments_order");
    }
}
