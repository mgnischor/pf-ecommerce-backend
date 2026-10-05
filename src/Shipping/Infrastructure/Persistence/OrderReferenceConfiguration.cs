using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Shipping.Domain;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>Mapping of <c>shipping.order_references</c>, the context's own record of whose each order is (BR-SHP-004).</summary>
internal sealed class OrderReferenceConfiguration : IEntityTypeConfiguration<OrderReference>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderReference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_references");

        builder.Property(reference => reference.CustomerId).IsRequired();
        builder.Property(reference => reference.Number).HasMaxLength(32).IsRequired();
    }
}
