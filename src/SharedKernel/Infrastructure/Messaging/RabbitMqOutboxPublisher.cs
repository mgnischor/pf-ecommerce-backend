using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// Publishes an outbox message to the events exchange and returns only when the broker has confirmed it (publisher
/// confirms, ai/CODE.md §5): a nack, a timeout or a lost connection throws, which leaves the message pending in the
/// outbox for the next attempt. The message carries its event id as <c>message-id</c> (consumers deduplicate on it), the
/// aggregate identity and version, the correlation and causation ids, and the W3C trace context of the relay's
/// <c>PRODUCER</c> span (ai/OBSERVABILITY.md §6.4). The channel is used by one publication at a time.
/// </summary>
internal sealed class RabbitMqOutboxPublisher(RabbitMqConnection connection, IOptions<RabbitMqOptions> options)
    : IOutboxPublisher,
        IAsyncDisposable
{
    /// <summary>Content type of the body.</summary>
    public const string ContentType = "application/json";

    private static readonly CreateChannelOptions ConfirmedChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true
    );

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        var routingKey = RoutingKeys.For(message.Type);
        var properties = BuildProperties(message, routingKey);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        Activity.Current?.SetTag("messaging.system", "rabbitmq");
        Activity.Current?.SetTag("messaging.destination.name", settings.Exchange);
        Activity.Current?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.PublishTimeout);

        await _gate.WaitAsync(timeout.Token);
        try
        {
            var channel = await GetChannelAsync(timeout.Token);

            // mandatory is off on purpose: an event nobody subscribed to is not an error (publish/subscribe).
            await channel.BasicPublishAsync(
                settings.Exchange,
                routingKey,
                mandatory: false,
                properties,
                body,
                timeout.Token
            );
        }
        catch
        {
            // A channel that failed mid-publication is in an unknown state: never reuse it.
            await DiscardChannelAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BasicProperties BuildProperties(OutboxMessage message, string routingKey)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["aggregate-id"] = message.AggregateId.ToString("D"),
            ["aggregate-version"] = message.AggregateVersion,
        };

        if (message.CausationId is not null)
        {
            headers["causation-id"] = message.CausationId;
        }

        // The relay's PRODUCER span is current here, so the consumer's span becomes its child.
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
        {
            headers["traceparent"] = activity.Id;
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                headers["tracestate"] = activity.TraceStateString;
            }
        }

        return new BasicProperties
        {
            MessageId = message.Id.ToString("D"),
            CorrelationId = message.CorrelationId,
            ContentType = ContentType,
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Type = routingKey,
            AppId = "pf-ecommerce-api",
            Headers = headers,
        };
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await DiscardChannelAsync();
        var open = await connection.GetAsync(cancellationToken);
        _channel = await open.CreateChannelAsync(ConfirmedChannel, cancellationToken);
        return _channel;
    }

    private async Task DiscardChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Already broken: the next publication opens a new channel.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DiscardChannelAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
