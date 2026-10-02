using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Portfolio.IntegrationTests.Caching;

/// <summary>
/// A disposable Valkey server (ai/TESTS.md §4.1): the image Compose runs, pinned by digest, started with a password and
/// no persistence, like production. The host port is fixed for the container's whole life, so a test can stop and start
/// the server and the client reconnects to the same address.
/// </summary>
public sealed class ValkeyContainer : IAsyncDisposable
{
    public const string Password = "valkey-password-for-tests-only";

    private const string Image =
        "valkey/valkey:9.1.2-alpine3.24@sha256:48332870af354a799964c0012ae1194a0bf2bf894eb508f945810596dc2d8d11";

    private readonly IContainer _container;

    private ValkeyContainer(IContainer container, int port)
    {
        _container = container;
        Port = port;
    }

    /// <summary>Host port of the server.</summary>
    internal int Port { get; }

    /// <summary>The connection string the application takes (<c>host:port,password=...</c>).</summary>
    internal string ConnectionString => $"127.0.0.1:{Port},password={Password}";

    internal static async Task<ValkeyContainer> StartAsync()
    {
        var port = FreePort();
        var container = new ContainerBuilder(Image)
            .WithPortBinding(port, 6379)
            .WithCommand("valkey-server", "--requirepass", Password, "--save", "", "--appendonly", "no")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await container.StartAsync();
        return new ValkeyContainer(container, port);
    }

    /// <summary>Stops the server; the port stays reserved for <see cref="StartServerAsync"/>.</summary>
    internal Task StopServerAsync() => _container.StopAsync();

    /// <summary>Starts the same container again on the same port.</summary>
    internal Task StartServerAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
