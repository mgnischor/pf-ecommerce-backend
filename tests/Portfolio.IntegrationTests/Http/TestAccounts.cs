namespace Portfolio.IntegrationTests.Http;

/// <summary>A bootstrap account created for the test run (random password, never committed).</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Random password.</param>
/// <param name="Level">Wire name of the access level.</param>
internal sealed record TestAccount(string Email, string Password, string Level);

/// <summary>The five access levels as they appear on the wire, lowest first.</summary>
internal static class TestAccounts
{
    public static readonly string[] AllLevels = ["public", "collaborator", "manager", "administrator", "developer"];

    /// <summary>Numeric rank of a level, so tests can compare without re-implementing the hierarchy.</summary>
    public static int Rank(string level) => Array.IndexOf(AllLevels, level);
}
