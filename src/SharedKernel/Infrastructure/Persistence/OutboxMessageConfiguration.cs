using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Mapping of <c>outbox_messages</c>. Documented exception to the traceability fields (ai/DATABASE.md §2.2):
/// a technical table with its own lifecycle columns.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>Name of the partial index the relay claims from.</summary>
    public const string PendingIndexName = "ix_outbox_messages_pending";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "outbox_messages",
            table =>
            {
                table.HasCheckConstraint("ck_outbox_messages_attempts", "attempts >= 0");
                table.HasCheckConstraint("ck_outbox_messages_aggregate_version", "aggregate_version >= 1");
            }
        );

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(300).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.AggregateId).IsRequired();
        builder.Property(message => message.AggregateVersion).IsRequired();
        builder.Property(message => message.OccurredAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(64);
        builder.Property(message => message.CausationId).HasMaxLength(64);
        builder.Property(message => message.ProcessedAt).HasColumnType("timestamptz");
        builder.Property(message => message.Attempts).IsRequired();
        builder.Property(message => message.LockedUntil).HasColumnType("timestamptz");
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);

        // The relay only ever reads unprocessed rows in order of occurrence.
        builder
            .HasIndex(message => message.OccurredAt)
            .HasDatabaseName(PendingIndexName)
            .HasFilter("processed_at IS NULL");
        builder.HasIndex(message => message.AggregateId).HasDatabaseName("ix_outbox_messages_aggregate_id");
    }
}
