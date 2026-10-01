using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Persistence abstraction for <see cref="User"/>. Defined in the domain; implemented in Infrastructure.</summary>
internal interface IUserRepository : IRepository<User>
{
    /// <summary>Finds an active (not deleted) account by e-mail, or <c>null</c>.</summary>
    /// <param name="email">Normalized e-mail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);
}
