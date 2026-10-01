namespace Portfolio.Identity.Application;

/// <summary>
/// Password verifier operations (ai/SECURITY.md §4.2). Implemented in Infrastructure with Argon2id; the
/// domain never sees a plaintext password or an algorithm.
/// </summary>
internal interface IPasswordHasher
{
    /// <summary>Computes a salted verifier in PHC string format.</summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> HashAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Checks a password against a stored verifier in constant time.</summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="passwordHash">Stored PHC string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends the same work as a real verification against a throw-away verifier, so the response time of a
    /// sign-in for an unknown or blocked account matches that of a wrong password.
    /// </summary>
    /// <param name="password">Plaintext password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BurnAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Whether the verifier was computed with weaker parameters than the current standard.</summary>
    /// <param name="passwordHash">Stored PHC string.</param>
    bool NeedsRehash(string passwordHash);
}
