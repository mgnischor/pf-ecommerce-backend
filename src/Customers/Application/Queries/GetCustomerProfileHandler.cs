using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Application;

/// <summary>
/// Reads the profile of the authenticated customer (BR-CUS-007). The only identifier it accepts is the caller's own, so
/// there is no object-level authorization to forget: a customer cannot name another customer's profile.
/// </summary>
internal sealed class GetCustomerProfileHandler(ICustomerProfileRepository profiles)
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">The caller's account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile, or <c>CUSTOMER_PROFILE_NOT_FOUND</c>.</returns>
    public async Task<Result<CustomerProfileView>> HandleAsync(
        GetCustomerProfileQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        var profile = await profiles.GetByIdAsync(query.AccountId, cancellationToken);

        return profile is null
            ? Result<CustomerProfileView>.Failure(CustomerErrors.ProfileNotFound)
            : Result<CustomerProfileView>.Success(profile.ToView());
    }
}
