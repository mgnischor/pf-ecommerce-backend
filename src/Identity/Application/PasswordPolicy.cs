using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// The user-facing password policy (BR-IDN-002, NIST SP 800-63B): 12 to 128 characters, no composition
/// rules, no forced rotation, screened against breached passwords and against the account's own name.
/// </summary>
internal sealed class PasswordPolicy(IBreachedPasswordScreen breachedPasswords)
{
    /// <summary>Minimum length.</summary>
    public const int MinLength = 12;

    /// <summary>Maximum length. Also bounds the work an unauthenticated caller can make the hasher do.</summary>
    public const int MaxLength = 128;

    private const int MinimumEmailNameLengthToCheck = 4;

    /// <summary>Checks a password a user wants to set.</summary>
    /// <param name="password">Candidate password.</param>
    /// <param name="email">Account e-mail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ValidateAsync(string? password, EmailAddress email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (password is null || password.Length is < MinLength or > MaxLength)
        {
            return Result.Failure(IdentityErrors.PasswordLength(MinLength, MaxLength));
        }

        var name = email.LocalPart;
        if (name.Length >= MinimumEmailNameLengthToCheck && password.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(IdentityErrors.PasswordContainsEmail);
        }

        return await breachedPasswords.IsBreachedAsync(password, cancellationToken)
            ? Result.Failure(IdentityErrors.PasswordCompromised)
            : Result.Success();
    }
}
