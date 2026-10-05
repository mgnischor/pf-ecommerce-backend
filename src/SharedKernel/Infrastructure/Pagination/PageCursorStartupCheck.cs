using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Builds the codec while the host starts, so a missing or weak key stops the instance instead of the first request.</summary>
/// <param name="codec">The cursor codec; constructing it validates the key.</param>
internal sealed class PageCursorStartupCheck(IPageCursorCodec codec) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Touching the instance proves it was constructed; construction is what validates the key.
        _ = codec;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
