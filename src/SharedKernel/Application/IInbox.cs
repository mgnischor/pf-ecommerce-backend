namespace Portfolio.SharedKernel.Application;

/// <summary>
/// Deduplicates at-least-once deliveries (ai/DATABASE.md §3.1, ai/ARCHITECTURE.md §8): a consumer registers the
/// message identifier in the same transaction as its side effect, so a redelivery finds the row and does nothing.
/// Implemented by each context's unit of work, so the inbox row commits atomically with the consumer's writes.
/// </summary>
internal interface IInbox
{
    /// <summary>
    /// Stages the message as processed by <paramref name="consumer"/>. The row is committed by the unit of work
    /// of the same use case; if a concurrent delivery commits it first, that commit raises
    /// <see cref="PersistenceConflictException"/> and nothing of this attempt is applied.
    /// </summary>
    /// <param name="messageId">Identifier of the message (the event ID).</param>
    /// <param name="consumer">Stable name of the consumer, so two consumers may handle the same message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the message is new for the consumer; <c>false</c> when it was already processed.</returns>
    Task<bool> TryBeginAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default);
}
