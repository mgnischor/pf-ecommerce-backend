using Microsoft.Extensions.Time.Testing;

namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// A running application whose clock the test controls, for behavior that depends on time: token and
/// session expiry, lockout windows. Every test signs in fresh, because advancing the clock ages the tokens of
/// the tests that ran before it.
/// </summary>
public sealed class ClockedAuthFixture : IAsyncLifetime
{
    internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

    internal ApiFactory Factory { get; private set; } = default!;

    internal HttpClient Client { get; private set; } = default!;

    public ValueTask InitializeAsync()
    {
        Factory = new ApiFactory("Production", clock: Clock);
        Client = Factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return ValueTask.CompletedTask;
    }
}
