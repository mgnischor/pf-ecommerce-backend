namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>A delivery as a consumer sees it: the envelope the publisher wrote, without any broker type.</summary>
/// <param name="MessageId">The event id (<c>message-id</c>): what the inbox deduplicates on.</param>
/// <param name="RoutingKey">The routing key the message was published with.</param>
/// <param name="CorrelationId">Business-flow correlation id, when the publisher had one.</param>
/// <param name="Redelivered">Whether the broker delivered this message before.</param>
/// <param name="Attempt">Delivery number, starting at 1; it grows each time the host requeues the message after a failure.</param>
/// <param name="Body">The JSON body. Valid only while the handler runs.</param>
internal sealed record ReceivedMessage(
    Guid MessageId,
    string RoutingKey,
    string? CorrelationId,
    bool Redelivered,
    int Attempt,
    ReadOnlyMemory<byte> Body
);
