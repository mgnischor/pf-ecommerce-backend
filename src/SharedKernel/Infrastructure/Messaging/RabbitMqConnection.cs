using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>
/// The one connection to RabbitMQ of the process (ai/CODE.md §5: one connection per process, channels per thread). It is
/// opened on first use, recovers by itself after a network failure, and declares the exchanges once per connection, so a
/// broker that is down when the application starts never fails the start: events wait in the outbox.
/// </summary>
internal sealed class RabbitMqConnection(
    IConfiguration configuration,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnection> logger
) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    /// <summary>Gets the connection, connecting (and declaring the exchanges) if there is none. It may be recovering: check <c>IsOpen</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // A connection that is down is not replaced: the client recovers it, and its channels and consumers with it.
            // Only one that this process closed is gone for good.
            if (_connection is not null && _connection.CloseReason?.Initiator != ShutdownInitiator.Application)
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            try
            {
                var connection = await CreateFactory().CreateConnectionAsync(cancellationToken);
                await DeclareTopologyAsync(connection, cancellationToken);
                _connection = connection;
                RabbitMqLog.Connected(logger);
                return connection;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RabbitMqLog.ConnectFailed(logger, exception.GetType().Name);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Builds the client settings from the secret connection string and the options.</summary>
    /// <exception cref="InvalidOperationException">The connection string is missing.</exception>
    private ConnectionFactory CreateFactory()
    {
        var connectionString = configuration.GetConnectionString(RabbitMqOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The message names the setting, never a value.
            throw new InvalidOperationException(
                $"The connection string '{RabbitMqOptions.ConnectionStringName}' is not configured (set ConnectionStrings__RabbitMQ)."
            );
        }

        return new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            ClientProvidedName = "pf-ecommerce-api",
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedConnectionTimeout = options.Value.ConnectTimeout,
        };
    }

    private async Task DeclareTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Declaring is idempotent, so every instance may do it on connect.
        await channel.ExchangeDeclareAsync(
            options.Value.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken
        );
        await channel.ExchangeDeclareAsync(
            options.Value.DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
