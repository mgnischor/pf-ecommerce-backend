using System.ComponentModel.DataAnnotations;
using Portfolio.SharedKernel.API.Contracts;

namespace Portfolio.Identity.API.Contracts;

/// <summary>Sign-in request. The password is never logged, echoed, or returned.</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Account password.</param>
internal sealed record CreateTokenRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password
);

/// <summary>Request to rotate a refresh token.</summary>
/// <param name="RefreshToken">The refresh token received with the previous token pair.</param>
internal sealed record RefreshTokenRequest([Required, StringLength(512, MinimumLength = 1)] string RefreshToken);

/// <summary>Request to end a session. Anonymous callers prove possession with the refresh token.</summary>
/// <param name="RefreshToken">Refresh token of the session to revoke; optional for authenticated callers.</param>
internal sealed record RevokeTokenRequest([StringLength(512)] string? RefreshToken = null);

/// <summary>Issued credentials.</summary>
/// <param name="AccessToken">Short-lived access token (ES384 JWT, <c>typ: at+jwt</c>).</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
/// <param name="RefreshToken">Single-use refresh token that rotates on every use.</param>
internal sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);

/// <summary>Self-registration of a customer. There is deliberately no way to name an access level.</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Chosen password: 12–128 characters, not breached, not containing the e-mail name.</param>
/// <param name="FullName">Optional name. It is handed to the customer profile; one that breaks its rules is left out, not rejected.</param>
/// <param name="Phone">Optional phone as an E.164 number such as <c>+5511987654321</c>.</param>
/// <param name="Locale">Optional preferred locale: <c>pt-BR</c> or <c>en</c>.</param>
/// <param name="TimeZone">Optional IANA time zone such as <c>America/Sao_Paulo</c>.</param>
internal sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password,
    [StringLength(512)] string? FullName = null,
    [StringLength(64)] string? Phone = null,
    [StringLength(32)] string? Locale = null,
    [StringLength(128)] string? TimeZone = null
);

/// <summary>The caller as the API sees them, from the validated token.</summary>
/// <param name="Id">Account identifier.</param>
/// <param name="AccessLevel">One of <c>public</c>, <c>collaborator</c>, <c>manager</c>, <c>administrator</c>, <c>developer</c>.</param>
internal sealed record CurrentUserResponse(Guid Id, string AccessLevel);

/// <summary>Request by an administrator to create a staff account.</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Initial password, subject to the password policy.</param>
/// <param name="AccessLevel">Level to grant: <c>collaborator</c>, <c>manager</c>, <c>administrator</c>, or <c>developer</c>; never above the caller's own.</param>
internal sealed record CreateUserRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password,
    [
        Required,
        RegularExpression(
            ContractPatterns.AccessLevelName,
            MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
        )
    ]
        string AccessLevel
);

/// <summary>Request to change an account's access level.</summary>
/// <param name="AccessLevel">New level; never above the caller's own.</param>
internal sealed record ChangeAccessLevelRequest(
    [
        Required,
        RegularExpression(
            ContractPatterns.AccessLevelName,
            MatchTimeoutInMilliseconds = ContractPatterns.TimeoutMilliseconds
        )
    ]
        string AccessLevel
);

/// <summary>Account as returned after creation.</summary>
/// <param name="Id">Account identifier.</param>
/// <param name="AccessLevel">Granted level.</param>
internal sealed record UserResponse(Guid Id, string AccessLevel);

/// <summary>A public signing key (RFC 7517).</summary>
/// <param name="Kty">Key type, <c>EC</c>.</param>
/// <param name="Crv">Curve, <c>P-384</c>.</param>
/// <param name="X">Base64url X coordinate.</param>
/// <param name="Y">Base64url Y coordinate.</param>
/// <param name="Kid">Key identifier referenced by token headers.</param>
/// <param name="Use">Always <c>sig</c>.</param>
/// <param name="Alg">Always <c>ES384</c>.</param>
internal sealed record JsonWebKeyResponse(
    string Kty,
    string Crv,
    string X,
    string Y,
    string Kid,
    string Use,
    string Alg
);

/// <summary>JSON Web Key Set of the token issuer.</summary>
/// <param name="Keys">Public keys, the active one and those kept for the maximum access-token lifetime after rotation.</param>
internal sealed record JsonWebKeySetResponse(IReadOnlyList<JsonWebKeyResponse> Keys);
