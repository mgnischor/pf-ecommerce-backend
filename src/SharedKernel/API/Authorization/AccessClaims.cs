namespace Portfolio.SharedKernel.API.Authorization;

/// <summary>
/// Claim names of the access token (RFC 7519 registered names plus two private ones). Tokens carry no
/// personal data: only opaque identifiers and the access level (ai/SECURITY.md §7.5).
/// </summary>
internal static class AccessClaims
{
    /// <summary>Identifier of the account (<c>sub</c>).</summary>
    public const string Subject = "sub";

    /// <summary>Unique identifier of the token (<c>jti</c>), used for revocation.</summary>
    public const string TokenId = "jti";

    /// <summary>Wire name of the access level, for example <c>manager</c>.</summary>
    public const string AccessLevel = "access_level";

    /// <summary>Version of the account's token set; a mismatch with the server value revokes the token.</summary>
    public const string TokenVersion = "ver";
}
