namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Sends an outbox message to the broker. Implemented by the messaging infrastructure (RabbitMQ).</summary>
internal interface IOutboxPublisher
{
    /// <summary>
    /// Publishes one message. Must be safe to repeat: the relay delivers at least once, and consumers deduplicate
    /// on <see cref="OutboxMessage.Id"/> through the inbox. Throwing leaves the message pending for a later attempt.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
