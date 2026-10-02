using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Runs every registered <see cref="IMessageConsumer"/> (ai/CODE.md §5, ai/ARCHITECTURE.md §9.3). Each consumer gets a
/// channel of its own, a durable quorum queue bound to its routing key, a dead-letter queue, a bounded prefetch and
/// <b>manual acknowledgements</b>: a message is acknowledged only after its handler committed. A handler that throws
/// has the message requeued after a growing pause until the queue's delivery limit dead-letters it; a message that can
/// never succeed (<see cref="PoisonMessageException"/>) is dead-lettered at once. Every delivery is a <c>CONSUMER</c>
/// span that is a child of the publisher's span (ai/OBSERVABILITY.md §6.4). On shutdown the consumers are cancelled
/// first, so no new message arrives, and in-flight handlers are given time to finish before the channels close.
/// </summary>
internal sealed class RabbitMqConsumerService : BackgroundService
{
    /// <summary>How long in-flight handlers may take to finish at shutdown; below the host's shutdown timeout.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Header that counts the deliveries of a message; the broker does not count requeues, so the host does.</summary>
    private const string AttemptHeader = "x-attempt";

    private static readonly CreateChannelOptions ConfirmedChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true
    );

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqConnection _connection;
    private readonly IReadOnlyList<IMessageConsumer> _consumers;
    private readonly IServiceScopeFactory _scopes;
    private readonly RabbitMqOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly Counter<long> _consumed;
    private readonly Histogram<double> _duration;
    private readonly CancellationTokenSource _handlers = new();
    private readonly List<(IChannel Channel, string Tag)> _subscriptions = [];
    private int _inFlight;

    /// <summary>Creates the host.</summary>
    /// <param name="connection">The shared connection.</param>
    /// <param name="consumers">Every registered consumer.</param>
    /// <param name="scopes">Creates the scope of one delivery.</param>
    /// <param name="options">Broker settings.</param>
    /// <param name="timeProvider">Source of time, for the retry pause.</param>
    /// <param name="meterFactory">Creates the consumer meter.</param>
    /// <param name="logger">Logger.</param>
    public RabbitMqConsumerService(
        RabbitMqConnection connection,
        IEnumerable<IMessageConsumer> consumers,
        IServiceScopeFactory scopes,
        IOptions<RabbitMqOptions> options,
        TimeProvider timeProvider,
        IMeterFactory meterFactory,
        ILogger<RabbitMqConsumerService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(consumers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _connection = connection;
        _consumers = [.. consumers];
        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        var meter = meterFactory.Create(TelemetryNames.MessagingMeter);
        _consumed = meter.CreateCounter<long>(
            "app.messaging.consumed",
            description: "Deliveries handled by a consumer, by outcome (processed, duplicate, retried, poisoned)."
        );
        _duration = meter.CreateHistogram<double>(
            "app.messaging.process.duration",
            unit: "s",
            description: "Duration of one delivery, from receipt to acknowledgement."
        );
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SubscribeWithRetryAsync(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await StopConsumingAsync();
        }
    }

    private async Task SubscribeWithRetryAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await _connection.GetAsync(stoppingToken);
                foreach (var consumer in _consumers)
                {
                    await SubscribeAsync(connection, consumer, stoppingToken);
                }

                RabbitMqLog.Consuming(_logger, _consumers.Count);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RabbitMqLog.SubscribeFailed(_logger, exception.GetType().Name);
                await CloseChannelsAsync();
                await Task.Delay(ReconnectDelay, _timeProvider, stoppingToken);
            }
        }
    }

    private async Task SubscribeAsync(
        IConnection connection,
        IMessageConsumer consumer,
        CancellationToken cancellationToken
    )
    {
        // Confirmations are on because a failed message is republished (see RetryAsync) before the original is acknowledged.
        var channel = await connection.CreateChannelAsync(ConfirmedChannel, cancellationToken);
        await DeclareQueuesAsync(channel, consumer, cancellationToken);
        await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken);

        var subscriber = new AsyncEventingBasicConsumer(channel);
        subscriber.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, consumer, delivery);

        var tag = await channel.BasicConsumeAsync(
            consumer.Name,
            autoAck: false,
            consumerTag: $"{consumer.Name}-{Guid.NewGuid():N}",
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: subscriber,
            cancellationToken
        );
        _subscriptions.Add((channel, tag));
    }

    /// <summary>
    /// Declares the topology of one consumer (idempotent): a dead-letter queue bound to the dead-letter exchange by the
    /// consumer's name, and the consumer's own quorum queue that dead-letters into it after the delivery limit.
    /// </summary>
    private async Task DeclareQueuesAsync(
        IChannel channel,
        IMessageConsumer consumer,
        CancellationToken cancellationToken
    )
    {
        var deadQueue = $"{consumer.Name}.dead";
        await channel.QueueDeclareAsync(
            deadQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            deadQueue,
            _options.DeadLetterExchange,
            consumer.Name,
            cancellationToken: cancellationToken
        );

        await channel.QueueDeclareAsync(
            consumer.Name,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = _options.MaxDeliveries,
                ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = consumer.Name,
                ["x-dead-letter-strategy"] = "at-least-once",
                ["x-overflow"] = "reject-publish",
            },
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            consumer.Name,
            _options.Exchange,
            consumer.BindingKey,
            cancellationToken: cancellationToken
        );
    }

    private async Task OnReceivedAsync(IChannel channel, IMessageConsumer consumer, BasicDeliverEventArgs delivery)
    {
        Interlocked.Increment(ref _inFlight);
        var started = Stopwatch.GetTimestamp();
        var outcome = "retried";

        try
        {
            outcome = await HandleDeliveryAsync(channel, consumer, delivery);
        }
        catch (OperationCanceledException) when (_handlers.IsCancellationRequested)
        {
            // Shutdown gave up on this handler: the unacknowledged message returns to the queue when the channel closes.
        }
        catch (Exception exception)
        {
            // The acknowledgement itself failed (the channel closed): the broker redelivers and the inbox deduplicates.
            RabbitMqLog.SettleFailed(_logger, consumer.Name, exception.GetType().Name);
        }
        finally
        {
            var consumerTag = new KeyValuePair<string, object?>("app.messaging.consumer", consumer.Name);
            var outcomeTag = new KeyValuePair<string, object?>(TelemetryNames.Outcome, outcome);
            _consumed.Add(1, consumerTag, outcomeTag);
            _duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, consumerTag, outcomeTag);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>Handles one delivery and settles it with the broker. Returns the outcome label.</summary>
    private async Task<string> HandleDeliveryAsync(
        IChannel channel,
        IMessageConsumer consumer,
        BasicDeliverEventArgs delivery
    )
    {
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
        {
            RabbitMqLog.Poisoned(_logger, consumer.Name, "MissingMessageId");
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
            return "poisoned";
        }

        using var activity = StartActivity(consumer, delivery, messageId);
        bool processed;
        try
        {
            var message = new ReceivedMessage(
                messageId,
                delivery.RoutingKey,
                delivery.BasicProperties.CorrelationId,
                delivery.Redelivered,
                AttemptOf(delivery),
                delivery.Body
            );

            await using var scope = _scopes.CreateAsyncScope();
            processed = await consumer.ConsumeAsync(message, scope.ServiceProvider, _handlers.Token);
        }
        catch (PoisonMessageException exception)
        {
            MarkFailed(activity, exception);
            RabbitMqLog.Poisoned(_logger, consumer.Name, exception.GetType().Name);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
            return "poisoned";
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !_handlers.IsCancellationRequested)
        {
            MarkFailed(activity, exception);
            var attempt = AttemptOf(delivery);
            RabbitMqLog.HandlerFailed(_logger, consumer.Name, attempt, exception.GetType().Name);

            if (attempt >= _options.MaxDeliveries)
            {
                RabbitMqLog.Poisoned(_logger, consumer.Name, "DeliveryLimitReached");
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, CancellationToken.None);
                return "poisoned";
            }

            await Task.Delay(RetryBackoff.For(_options.RetryBackoff, attempt), _timeProvider, _handlers.Token);
            await RetryAsync(channel, consumer, delivery, attempt + 1);
            return "retried";
        }

        activity?.SetTag("app.messaging.duplicate", !processed);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        return processed ? "processed" : "duplicate";
    }

    private static Activity? StartActivity(IMessageConsumer consumer, BasicDeliverEventArgs delivery, Guid messageId)
    {
        var parent = default(ActivityContext);
        var traceParent = HeaderText(delivery.BasicProperties, "traceparent");
        if (
            traceParent is not null
            && !ActivityContext.TryParse(
                traceParent,
                HeaderText(delivery.BasicProperties, "tracestate"),
                isRemote: true,
                out parent
            )
        )
        {
            parent = default; // A malformed context is ignored: the span starts a new trace.
        }

        var activity = OutboxTracing.Source.StartActivity($"process {consumer.Name}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", consumer.Name);
        activity?.SetTag("messaging.message.id", messageId.ToString("D"));
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", delivery.RoutingKey);
        activity?.SetTag("app.correlation_id", delivery.BasicProperties.CorrelationId);
        activity?.SetTag("app.messaging.redelivered", delivery.Redelivered);
        return activity;
    }

    private static void MarkFailed(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag("error.type", exception.GetType().Name);
    }

    /// <summary>Which delivery this is, starting at 1: a message the host republished carries its own count.</summary>
    private static int AttemptOf(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers is { } headers
        && headers.TryGetValue(AttemptHeader, out var value)
        && value is int attempt
        && attempt > 0
            ? attempt
            : 1;

    /// <summary>
    /// Puts a failed message back at the end of its own queue with a higher attempt count, then acknowledges the
    /// original. The copy is confirmed by the broker first, so a crash in between duplicates the message (the inbox
    /// absorbs that) and never loses it. It goes to the default exchange by queue name: other consumers' queues, bound to
    /// the same routing key, do not receive it again.
    /// </summary>
    private static async Task RetryAsync(
        IChannel channel,
        IMessageConsumer consumer,
        BasicDeliverEventArgs delivery,
        int attempt
    )
    {
        var original = delivery.BasicProperties;
        var headers = new Dictionary<string, object?>(
            original.Headers ?? new Dictionary<string, object?>(StringComparer.Ordinal),
            StringComparer.Ordinal
        )
        {
            [AttemptHeader] = attempt,
        };

        var copy = new BasicProperties
        {
            MessageId = original.MessageId,
            CorrelationId = original.CorrelationId,
            ContentType = original.ContentType,
            ContentEncoding = original.ContentEncoding,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = original.Timestamp,
            Type = original.Type,
            AppId = original.AppId,
            Headers = headers,
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: consumer.Name,
            mandatory: true,
            copy,
            delivery.Body,
            CancellationToken.None
        );
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
    }

    private static string? HeaderText(IReadOnlyBasicProperties properties, string name) =>
        properties.Headers is { } headers && headers.TryGetValue(name, out var value) && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;

    private async Task StopConsumingAsync()
    {
        // 1. Stop new deliveries.
        foreach (var (channel, tag) in _subscriptions)
        {
            try
            {
                await channel.BasicCancelAsync(tag, noWait: false, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The channel is already gone: nothing is being delivered through it.
            }
        }

        // 2. Let in-flight handlers finish, then give up on the ones that do not.
        var deadline = _timeProvider.GetTimestamp();
        while (Volatile.Read(ref _inFlight) > 0 && _timeProvider.GetElapsedTime(deadline) < DrainTimeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, CancellationToken.None);
        }

        await _handlers.CancelAsync();

        // 3. Close the channels; whatever is still unacknowledged goes back to its queue.
        await CloseChannelsAsync();
    }

    private async Task CloseChannelsAsync()
    {
        foreach (var (channel, _) in _subscriptions)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Already closed.
            }
        }

        _subscriptions.Clear();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _handlers.Dispose();
        base.Dispose();
    }
}
