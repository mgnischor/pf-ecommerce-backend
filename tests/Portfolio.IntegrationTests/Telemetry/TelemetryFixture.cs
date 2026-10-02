using Portfolio.IntegrationTests.Http;

namespace Portfolio.IntegrationTests.Telemetry;

/// <summary>One running application with a telemetry capture attached and a signed-in token for every access level.</summary>
public sealed class TelemetryFixture : IAsyncLifetime
{
    private readonly Dictionary<string, IssuedTokens> _tokens = new(StringComparer.Ordinal);

    internal TelemetryCapture Capture { get; } = new();

    internal ApiFactory Factory { get; private set; } = default!;

    internal HttpClient Client { get; private set; } = default!;

    internal IssuedTokens TokenFor(string level) => _tokens[level];

    public async ValueTask InitializeAsync()
    {
        Factory = new ApiFactory("Production", configure: Capture.Attach);
        Client = Factory.CreateClient();
        foreach (var account in Factory.Accounts.Values)
        {
            _tokens[account.Level] = await AuthClient.SignInAsync(Client, account);
        }
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();
        return ValueTask.CompletedTask;
    }
}
