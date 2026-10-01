using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Mapping of <c>inbox_messages</c>: the composite key is the deduplication guarantee.</summary>
internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inbox_messages");

        builder.HasKey(message => new { message.Consumer, message.MessageId });
        builder.Property(message => message.Consumer).HasMaxLength(InboxMessage.MaxConsumerLength).IsRequired();
        builder.Property(message => message.MessageId).ValueGeneratedNever();
        builder.Property(message => message.ProcessedAt).HasColumnType("timestamptz").IsRequired();

        // The purge job deletes by age.
        builder.HasIndex(message => message.ProcessedAt).HasDatabaseName("ix_inbox_messages_processed_at");
    }
}
