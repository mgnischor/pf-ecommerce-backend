namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Settings of the RabbitMQ access (ai/ARCHITECTURE.md §9.3). The connection string is the secret
/// <c>ConnectionStrings:RabbitMQ</c> (<c>amqp://user:password@host:5672</c>) and never lives here.
/// </summary>
internal sealed class RabbitMqOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>Name of the connection string (<c>ConnectionStrings:RabbitMQ</c>).</summary>
    public const string ConnectionStringName = "RabbitMQ";

    /// <summary>The durable topic exchange every integration event is published to.</summary>
    public string Exchange { get; init; } = "ecommerce.events";

    /// <summary>
    /// The direct exchange consumer queues dead-letter into (routing key = the queue name, so each queue has its own
    /// <c>{queue}.dead</c>); declared with the main one so a consumer can rely on it.
    /// </summary>
    public string DeadLetterExchange { get; init; } = "ecommerce.events.dead";

    /// <summary>How long the publisher waits for the broker to confirm one message before treating it as failed.</summary>
    public TimeSpan PublishTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a connection attempt may take.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Whether this process consumes messages. On only in the worker role.</summary>
    public bool ConsumersEnabled { get; init; }

    /// <summary>Unacknowledged messages the broker sends a consumer at once (<c>BasicQos</c>); bounds memory and in-flight work.</summary>
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    /// Deliveries after which a message that keeps failing is dead-lettered. The host counts them (the broker does not count
    /// requeues); the same number is the queue's own delivery limit, which catches a message that crashes the consumer.
    /// </summary>
    public int MaxDeliveries { get; init; } = 5;

    /// <summary>Wait before a failed message is requeued; doubles with each delivery up to 30 seconds.</summary>
    public TimeSpan RetryBackoff { get; init; } = TimeSpan.FromSeconds(2);
}
