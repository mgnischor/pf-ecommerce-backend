namespace Portfolio.Identity.Application;

/// <summary>Screens new passwords against a corpus of breached or trivially guessable ones (NIST SP 800-63B).</summary>
internal interface IBreachedPasswordScreen
{
    /// <summary>Whether the password appears in the corpus.</summary>
    /// <param name="password">Candidate password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}
