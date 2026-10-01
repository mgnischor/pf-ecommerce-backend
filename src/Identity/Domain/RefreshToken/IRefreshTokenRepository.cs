using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Domain;

/// <summary>Persistence abstraction for <see cref="RefreshToken"/>. Defined in the domain; implemented in Infrastructure.</summary>
internal interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    /// <summary>Finds a token by the HMAC of its opaque value, or <c>null</c>.</summary>
    /// <param name="tokenHash">Base64url HMAC-SHA3-512.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Lists every token of a session.</summary>
    /// <param name="familyId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RefreshToken>> ListByFamilyAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>Lists every token of an account, across sessions.</summary>
    /// <param name="userId">Account identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RefreshToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
