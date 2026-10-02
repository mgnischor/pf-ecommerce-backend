namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// One consumer of integration events (ai/ARCHITECTURE.md §9.3): a durable queue of its own, bound to a routing key of
/// the events exchange, and a handler. Delivery is at least once, so a handler deduplicates on the message id through
/// the inbox of its context, in the same transaction as its side effect (<see cref="Application.IInbox"/>).
/// </summary>
internal interface IMessageConsumer
{
    /// <summary>
    /// Stable name: the queue name, the inbox consumer name and the telemetry label. Renaming it abandons the old queue
    /// and forgets what the inbox knows, so it never changes after release. Lowercase, dot-separated.
    /// </summary>
    string Name { get; }

    /// <summary>Routing key the queue is bound to (<c>catalog.product-created</c>); the host declares the binding.</summary>
    string BindingKey { get; }

    /// <summary>
    /// Handles one delivery. Throwing <see cref="PoisonMessageException"/> sends the message straight to the dead-letter
    /// queue; any other exception requeues it, up to the queue's delivery limit.
    /// </summary>
    /// <param name="message">The delivery.</param>
    /// <param name="services">Services of a scope that lives for this delivery only.</param>
    /// <param name="cancellationToken">Cancelled when the host gives up waiting for the handler at shutdown.</param>
    /// <returns><c>true</c> when the message was handled now; <c>false</c> when the inbox showed it was handled before.</returns>
    Task<bool> ConsumeAsync(ReceivedMessage message, IServiceProvider services, CancellationToken cancellationToken);
}
