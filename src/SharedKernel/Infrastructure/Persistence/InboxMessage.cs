namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// One row of a context's inbox (ai/DATABASE.md §3.1): proof that <see cref="Consumer"/> already handled
/// <see cref="MessageId"/>. The composite primary key is what makes a duplicate delivery impossible to commit.
/// A technical table, not a domain entity.
/// </summary>
internal sealed class InboxMessage
{
    /// <summary>Longest consumer name.</summary>
    public const int MaxConsumerLength = 200;

    /// <summary>Identifier of the handled message (the event ID).</summary>
    public Guid MessageId { get; private set; }

    /// <summary>Stable name of the consumer that handled it.</summary>
    public string Consumer { get; private set; }

    /// <summary>UTC instant the message was handled; expired rows are purged by a scheduled job.</summary>
    public DateTimeOffset ProcessedAt { get; private set; }

    /// <summary>EF Core constructor. Do not use in application code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private InboxMessage() { }
#pragma warning restore CS8618

    /// <summary>Records a handled message.</summary>
    /// <param name="messageId">Message identifier.</param>
    /// <param name="consumer">Consumer name.</param>
    /// <param name="processedAt">UTC instant of handling.</param>
    public static InboxMessage Record(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        if (consumer.Length > MaxConsumerLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consumer),
                $"A consumer name has at most {MaxConsumerLength} characters."
            );
        }

        return new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedAt = processedAt,
        };
    }
}
