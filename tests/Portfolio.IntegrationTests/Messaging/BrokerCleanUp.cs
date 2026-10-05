namespace Portfolio.IntegrationTests.Messaging;

/// <summary>
/// Deletes the queues and the exchanges a worker host declared when the test ends, whether it passed or not: the queues
/// are named after their consumer, so one left behind (bound to another test's dead-letter exchange) would make the
/// next host fail to declare it.
/// </summary>
internal sealed class BrokerCleanUp(string suffix) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await WorkerRoleTests.CleanUpAsync(suffix);
}
