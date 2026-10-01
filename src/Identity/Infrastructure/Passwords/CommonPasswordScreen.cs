using System.Collections.Frozen;
using Portfolio.Identity.Application;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Minimal screen against trivially guessable passwords. It is a stand-in for an offline copy of the
/// Have I Been Pwned range corpus (ai/SECURITY.md §4.2), which is the intended implementation of
/// <see cref="IBreachedPasswordScreen"/>; until then it rejects the most common 12+ character choices and
/// passwords built from one or two distinct characters.
/// </summary>
internal sealed class CommonPasswordScreen : IBreachedPasswordScreen
{
    private const int MinDistinctCharacters = 3;

    private static readonly FrozenSet<string> Common = new[]
    {
        "password1234",
        "password12345",
        "password123456",
        "passwordpassword",
        "p@ssw0rd1234",
        "p@ssword1234",
        "qwerty123456",
        "qwertyuiop12",
        "qwertyuiopas",
        "123456789012",
        "1234567890123",
        "123456123456",
        "abcdefghijkl",
        "abc123abc123",
        "iloveyou1234",
        "letmein12345",
        "welcome12345",
        "changeme1234",
        "changemenow1",
        "administrator",
        "adminadmin12",
        "admin1234567",
        "trustno1trustno1",
        "correct horse battery staple",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        var breached = Common.Contains(password) || password.Distinct().Count() < MinDistinctCharacters;
        return Task.FromResult(breached);
    }
}
