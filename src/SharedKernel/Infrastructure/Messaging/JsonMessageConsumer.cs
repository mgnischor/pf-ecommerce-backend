using System.Text.Json;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// A consumer whose message is a JSON event. The message type belongs to the consuming context: it declares only the
/// fields the context needs, so it never references the publisher's types (ai/ARCHITECTURE.md §2.3.3) and tolerates
/// fields added later (backward-compatible evolution of the contract).
/// </summary>
/// <typeparam name="TMessage">The consumer's own view of the event.</typeparam>
internal abstract class JsonMessageConsumer<TMessage> : IMessageConsumer
    where TMessage : class
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string BindingKey { get; }

    /// <inheritdoc />
    public Task<bool> ConsumeAsync(
        ReceivedMessage message,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(services);

        TMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TMessage>(message.Body.Span, MessageJson.Options);
        }
        catch (JsonException exception)
        {
            throw new PoisonMessageException("The message body is not valid JSON for this consumer.", exception);
        }

        return payload is null
            ? throw new PoisonMessageException("The message body is empty.")
            : HandleAsync(message, payload, services, cancellationToken);
    }

    /// <summary>Handles the deserialized event.</summary>
    /// <param name="message">The delivery envelope.</param>
    /// <param name="payload">The event.</param>
    /// <param name="services">Services of the delivery's scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when handled now; <c>false</c> when it was a duplicate.</returns>
    protected abstract Task<bool> HandleAsync(
        ReceivedMessage message,
        TMessage payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    );
}
