using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Rotates a refresh token (BR-IDN-005). The presented token is consumed and its successor issued in the
/// same family. Presenting a token that was already consumed means it leaked: the whole family is revoked and
/// every access token of the account is invalidated, yet the client only ever sees <c>INVALID_REFRESH_TOKEN</c>.
/// </summary>
internal sealed class RefreshTokenHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IRefreshTokenCodec codec,
    TokenPairFactory tokenFactory,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    private const int MaxTokenLength = 512;

    /// <summary>Executes the command.</summary>
    /// <param name="command">The presented token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<TokenPair>> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrEmpty(command.RefreshToken) || command.RefreshToken.Length > MaxTokenLength)
        {
            return Result<TokenPair>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        var token = await refreshTokens.FindByHashAsync(codec.Hash(command.RefreshToken), cancellationToken);
        if (token is null)
        {
            return Result<TokenPair>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        var used = token.Use(timeProvider);
        if (used.IsFailure)
        {
            return await RejectAsync(token, used.Error, cancellationToken);
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeFamilyAsync(token.FamilyId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TokenPair>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        var pair = await tokenFactory.IssueAsync(user, token.FamilyId, token.FamilyExpiresAt, cancellationToken);
        refreshTokens.Update(token);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<TokenPair>.Success(pair);
    }

    private async Task<Result<TokenPair>> RejectAsync(
        RefreshToken token,
        Error reason,
        CancellationToken cancellationToken
    )
    {
        if (string.Equals(reason.Code, RefreshTokenErrors.Reused.Code, StringComparison.Ordinal))
        {
            await RevokeFamilyAsync(token.FamilyId, cancellationToken);

            var owner = await users.GetByIdAsync(token.UserId, cancellationToken);
            if (owner is not null)
            {
                owner.RevokeAllTokens(timeProvider);
                users.Update(owner);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            audit.RefreshTokenReuseDetected(token.UserId, token.FamilyId);
        }

        return Result<TokenPair>.Failure(IdentityErrors.InvalidRefreshToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        foreach (var member in await refreshTokens.ListByFamilyAsync(familyId, cancellationToken))
        {
            member.Revoke(timeProvider);
            refreshTokens.Update(member);
        }
    }
}
