namespace Portfolio.IntegrationTests.Http;

/// <summary>
/// One running application shared by the tests of a class, with a signed-in token for every access level.
/// Signing in costs an Argon2id verification, so it happens once per class instead of once per test.
/// </summary>
public sealed class AuthFixture : IAsyncLifetime
{
    private readonly Dictionary<string, IssuedTokens> _tokens = new(StringComparer.Ordinal);

    internal ApiFactory Factory { get; } = new("Production");

    internal HttpClient Client { get; private set; } = default!;

    internal IssuedTokens TokenFor(string level) => _tokens[level];

    public async ValueTask InitializeAsync()
    {
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
