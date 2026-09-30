using System.ComponentModel.DataAnnotations;

namespace Portfolio.Identity.API.Contracts;

/// <summary>Sign-in request. The password is never logged, echoed, or returned.</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Account password.</param>
public sealed record CreateTokenRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 1)] string Password
);

/// <summary>Issued credentials.</summary>
/// <param name="AccessToken">Short-lived access token.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
/// <param name="RefreshToken">Single-use refresh token that rotates on every use.</param>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);
