using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Issues the access and refresh token of a session step. Shared by sign-in (new family) and refresh
/// (same family, never beyond its absolute expiry).
/// </summary>
internal sealed class TokenPairFactory(
    IAccessTokenIssuer accessTokens,
    IRefreshTokenCodec refreshCodec,
    IRefreshTokenRepository refreshTokens,
    TokenLifetimes lifetimes,
    TimeProvider timeProvider
)
{
    /// <summary>Stages a new refresh token in the family and signs an access token.</summary>
    /// <param name="user">Account the tokens belong to.</param>
    /// <param name="familyId">Session identifier.</param>
    /// <param name="familyExpiresAt">Absolute expiry of the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TokenPair> IssueAsync(
        User user,
        Guid familyId,
        DateTimeOffset familyExpiresAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = timeProvider.GetUtcNow();
        var expiresAt = Min(now + lifetimes.Refresh, familyExpiresAt);

        var generated = refreshCodec.Generate();
        var refreshToken = RefreshToken.Issue(
            user.Id,
            familyId,
            generated.Hash,
            expiresAt,
            familyExpiresAt,
            timeProvider
        );
        await refreshTokens.AddAsync(refreshToken, cancellationToken);

        var access = accessTokens.Issue(user);
        return new TokenPair(access.Token, (int)(access.ExpiresAt - now).TotalSeconds, generated.Plain);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}
