using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Stable errors of the Identity rules (docs/business-rules/identity.md).</summary>
internal static class IdentityErrors
{
    /// <summary>
    /// Sign-in failed. One generic error for unknown account, wrong password, locked, and deactivated
    /// accounts, so the response never reveals which check failed (no account enumeration, BR-IDN-003).
    /// </summary>
    public static Error InvalidCredentials => Error.Unauthorized("INVALID_CREDENTIALS", "BR-IDN-003");

    /// <summary>The refresh token is unknown, expired, revoked, or reused (BR-IDN-005).</summary>
    public static Error InvalidRefreshToken => Error.Unauthorized("INVALID_REFRESH_TOKEN", "BR-IDN-005");

    /// <summary>BR-IDN-001: the e-mail is not a valid address.</summary>
    public static Error EmailInvalid => Error.Validation("EMAIL_INVALID", "email", "BR-IDN-001");

    /// <summary>BR-IDN-001: another account already uses the e-mail.</summary>
    public static Error EmailAlreadyRegistered => Error.Conflict("EMAIL_ALREADY_REGISTERED", "BR-IDN-001");

    /// <summary>BR-IDN-002: the password length is outside the allowed range.</summary>
    public static Error PasswordLength(int min, int max) =>
        Error.Validation("PASSWORD_LENGTH", "password", "BR-IDN-002", ErrorParameters.Of(("min", min), ("max", max)));

    /// <summary>BR-IDN-002: the password appears in a corpus of breached or trivially guessable passwords.</summary>
    public static Error PasswordCompromised => Error.Validation("PASSWORD_COMPROMISED", "password", "BR-IDN-002");

    /// <summary>BR-IDN-002: the password contains the account's own e-mail name.</summary>
    public static Error PasswordContainsEmail => Error.Validation("PASSWORD_CONTAINS_EMAIL", "password", "BR-IDN-002");

    /// <summary>BR-IDN-004: the access level is not one of the five defined levels, or is not assignable here.</summary>
    public static Error AccessLevelInvalid => Error.Validation("ACCESS_LEVEL_INVALID", "accessLevel", "BR-IDN-004");

    /// <summary>BR-IDN-004: the caller's level is too low for the action.</summary>
    public static Error AccessLevelInsufficient => Error.Forbidden("ACCESS_LEVEL_INSUFFICIENT", "BR-IDN-004");

    /// <summary>BR-IDN-004: the caller tried to grant a level above their own.</summary>
    public static Error AccessLevelEscalation => Error.Forbidden("ACCESS_LEVEL_ESCALATION", "BR-IDN-004");

    /// <summary>BR-IDN-004: the target account is not strictly below the caller and cannot be managed.</summary>
    public static Error UserNotManageable => Error.Forbidden("USER_NOT_MANAGEABLE", "BR-IDN-004");

    /// <summary>The account does not exist.</summary>
    public static Error UserNotFound => Error.NotFound("USER_NOT_FOUND");
}
