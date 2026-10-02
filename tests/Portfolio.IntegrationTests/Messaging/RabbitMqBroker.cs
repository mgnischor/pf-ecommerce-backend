using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// A disposable RabbitMQ broker (ai/TESTS.md §4.1): the image Compose runs, pinned by digest, with a generated
/// application user. The host port is fixed for the container's whole life, so a test can stop and start the broker and
/// the client recovers on the same address.
/// </summary>
public sealed class RabbitMqBroker : IAsyncDisposable
{
    public const string User = "app";
    public const string Password = "rabbit-password-for-tests-only";

    private const string Image =
        "rabbitmq:4.3.6-management-alpine@sha256:95312f53d5b1115d410f1a56fba03a6325e3e03b4c60a845237bd345f341a097";

    private readonly IContainer _container;

    private RabbitMqBroker(IContainer container, int port)
    {
        _container = container;
        Port = port;
    }

    /// <summary>Host port of the broker.</summary>
    internal int Port { get; }

    /// <summary>The connection string the application takes (<c>amqp://user:password@host:port</c>).</summary>
    internal string ConnectionString => $"amqp://{User}:{Password}@127.0.0.1:{Port}";

    /// <summary>A connection string for a port nobody listens on, to test an unreachable broker.</summary>
    internal static string Unreachable => $"amqp://{User}:{Password}@127.0.0.1:{FreePort()}";

    internal static async Task<RabbitMqBroker> StartAsync()
    {
        var port = FreePort();
        var container = new ContainerBuilder(Image)
            .WithPortBinding(port, 5672)
            .WithEnvironment("RABBITMQ_DEFAULT_USER", User)
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", Password)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
            .Build();
        await container.StartAsync();
        return new RabbitMqBroker(container, port);
    }

    /// <summary>Stops the broker; the port stays reserved for <see cref="StartBrokerAsync"/>.</summary>
    internal Task StopBrokerAsync() => _container.StopAsync();

    /// <summary>Starts the same container again on the same port.</summary>
    internal Task StartBrokerAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
