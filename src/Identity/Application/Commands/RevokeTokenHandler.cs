using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Signs out (RFC 7009 semantics): revokes the session behind a refresh token and blocks the caller's
/// current access token. Always succeeds, so the endpoint cannot be used to test whether a token is valid.
/// </summary>
internal sealed class RevokeTokenHandler(
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenCodec codec,
    IRevokedTokenStore revokedTokens,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    private const int MaxTokenLength = 512;

    /// <summary>Executes the command.</summary>
    /// <param name="command">What to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> HandleAsync(RevokeTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid? owner = command.UserId;

        if (!string.IsNullOrEmpty(command.RefreshToken) && command.RefreshToken.Length <= MaxTokenLength)
        {
            var token = await refreshTokens.FindByHashAsync(codec.Hash(command.RefreshToken), cancellationToken);
            if (token is not null)
            {
                owner ??= token.UserId;
                foreach (var member in await refreshTokens.ListByFamilyAsync(token.FamilyId, cancellationToken))
                {
                    member.Revoke(timeProvider);
                    refreshTokens.Update(member);
                }
            }
        }

        if (!string.IsNullOrEmpty(command.AccessTokenId) && command.AccessTokenExpiresAt is { } expiresAt)
        {
            await revokedTokens.RevokeAsync(command.AccessTokenId, expiresAt, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        audit.TokenRevoked(owner);

        return Result.Success();
    }
}
