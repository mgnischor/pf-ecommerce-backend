using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>Creates a staff account on behalf of an administrator (BR-IDN-004: privileges only flow downward).</summary>
internal sealed class CreateUserHandler(
    IUserRepository users,
    PasswordPolicy passwordPolicy,
    IPasswordHasher passwordHasher,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Account data and the caller's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<Guid>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AccessLevels.TryParseWireName(command.AccessLevel, out var level) || level == AccessLevel.Public)
        {
            return Denied(command, Result<Guid>.Failure(IdentityErrors.AccessLevelInvalid));
        }

        var grant = AccessManagementRules.CanGrant(command.ActorLevel, level);
        if (grant.IsFailure)
        {
            return Denied(command, Result<Guid>.Failure(grant.Error));
        }

        var email = EmailAddress.Create(command.Email);
        if (email.IsFailure)
        {
            return Result<Guid>.Failure(email.Error);
        }

        var policy = await passwordPolicy.ValidateAsync(command.Password, email.Value, cancellationToken);
        if (policy.IsFailure)
        {
            return Result<Guid>.Failure(policy.Error);
        }

        if (await users.FindByEmailAsync(email.Value, cancellationToken) is not null)
        {
            return Result<Guid>.Failure(IdentityErrors.EmailAlreadyRegistered);
        }

        // The policy check guarantees a non-null password.
        var hash = await passwordHasher.HashAsync(command.Password!, cancellationToken);
        var user = User.Register(email.Value, hash, level, timeProvider);

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        audit.AccountCreated(user.Id, user.AccessLevel, command.ActorId);

        return Result<Guid>.Success(user.Id);
    }

    private Result<Guid> Denied(CreateUserCommand command, Result<Guid> failure)
    {
        audit.PrivilegeDenied(command.ActorId, failure.Error!.Code);
        return failure;
    }
}
