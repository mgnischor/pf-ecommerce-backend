using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Application;

/// <summary>
/// Self-registration of a customer. Always creates a <see cref="AccessLevel.Public"/> account: the request
/// has no way to name a level, so it can never be used to escalate privileges (mass assignment, API3).
/// </summary>
internal sealed class RegisterCustomerHandler(
    IUserRepository users,
    PasswordPolicy passwordPolicy,
    IPasswordHasher passwordHasher,
    ISecurityAudit audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider
)
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">Account data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new account identifier, or the violated rule.</returns>
    public async Task<Result<Guid>> HandleAsync(RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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
        var user = User.Register(email.Value, hash, AccessLevel.Public, timeProvider);

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        audit.AccountCreated(user.Id, user.AccessLevel, actorId: null);

        return Result<Guid>.Success(user.Id);
    }
}
