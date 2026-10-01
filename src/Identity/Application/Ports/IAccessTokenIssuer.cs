using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Application;

/// <summary>Issues short-lived access tokens (ai/SECURITY.md §7). Implemented in Infrastructure.</summary>
internal interface IAccessTokenIssuer
{
    /// <summary>Signs an access token for an account at its current access level and token version.</summary>
    /// <param name="user">The authenticated account.</param>
    IssuedAccessToken Issue(User user);
}
