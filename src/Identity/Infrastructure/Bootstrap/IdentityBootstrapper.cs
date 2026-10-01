using Microsoft.Extensions.Options;
using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Creates the configured bootstrap accounts before the host accepts requests. Idempotent (existing
/// e-mails are left untouched) and fail closed: a malformed or policy-violating entry stops the startup
/// instead of creating a weak privileged account.
/// </summary>
internal sealed class IdentityBootstrapper(
    IOptions<IdentityBootstrapOptions> options,
    IServiceScopeFactory scopes,
    PasswordPolicy passwordPolicy,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<IdentityBootstrapper> logger
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var created = 0;

        foreach (var account in options.Value.Accounts)
        {
            if (await EnsureAsync(account, cancellationToken))
            {
                created++;
            }
        }

        BootstrapLog.Completed(logger, created, options.Value.Accounts.Count);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> EnsureAsync(BootstrapAccount account, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(account.Email);
        if (email.IsFailure)
        {
            throw new InvalidOperationException("A bootstrap account has an invalid e-mail.");
        }

        if (!AccessLevels.TryParseWireName(account.AccessLevel, out var level))
        {
            throw new InvalidOperationException("A bootstrap account has an unknown access level.");
        }

        // A hosted service is a singleton: every account gets its own scope, hence its own database context.
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        if (await users.FindByEmailAsync(email.Value, cancellationToken) is not null)
        {
            return false;
        }

        var policy = await passwordPolicy.ValidateAsync(account.Password, email.Value, cancellationToken);
        if (policy.IsFailure)
        {
            throw new InvalidOperationException(
                $"A bootstrap account password violates the policy ({policy.Error.Code})."
            );
        }

        // The policy check guarantees a non-null password.
        var hash = await passwordHasher.HashAsync(account.Password!, cancellationToken);
        await users.AddAsync(User.Register(email.Value, hash, level, timeProvider), cancellationToken);

        try
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // Another replica starting at the same time created it first: the account exists, which is the goal.
            return false;
        }

        return true;
    }
}
