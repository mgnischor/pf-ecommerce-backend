using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Authenticates with e-mail and password (BR-IDN-003). Every failure mode — malformed e-mail, unknown
/// account, wrong password, locked or deactivated account — returns the same error after the same amount of
/// work, so neither the body nor the timing reveals whether an account exists.
/// </summary>
internal sealed class SignInHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    TokenPairFactory tokenFactory,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TokenLifetimes lifetimes,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<TokenPair>> HandleAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var password = command.Password ?? string.Empty;
        var email = EmailAddress.Create(command.Email);
        var user = email.IsSuccess ? await users.FindByEmailAsync(email.Value, cancellationToken) : null;
        var now = timeProvider.GetUtcNow();

        if (user is null || !user.CanSignIn(now) || password.Length > PasswordPolicy.MaxLength)
        {
            await passwordHasher.BurnAsync(password, cancellationToken);
            audit.SignInFailed(user?.Id, user?.IsLockedOut(now) ?? false);
            return Result<TokenPair>.Failure(IdentityErrors.InvalidCredentials);
        }

        if (!await passwordHasher.VerifyAsync(password, user.PasswordHash, cancellationToken))
        {
            return await RejectAsync(user, cancellationToken);
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.ReplacePasswordHash(await passwordHasher.HashAsync(password, cancellationToken), timeProvider);
        }

        user.RecordSuccessfulSignIn(timeProvider);
        var pair = await tokenFactory.IssueAsync(
            user,
            Entity.NewId(timeProvider),
            now + lifetimes.RefreshFamily,
            cancellationToken
        );

        users.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        audit.SignInSucceeded(user.Id);

        return Result<TokenPair>.Success(pair);
    }

    private async Task<Result<TokenPair>> RejectAsync(User user, CancellationToken cancellationToken)
    {
        user.RecordFailedSignIn(timeProvider);
        users.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        audit.SignInFailed(user.Id, lockedOut: false);
        if (user.IsLockedOut(timeProvider.GetUtcNow()))
        {
            audit.AccountLocked(user.Id);
        }

        return Result<TokenPair>.Failure(IdentityErrors.InvalidCredentials);
    }
}
