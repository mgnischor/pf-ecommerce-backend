using Portfolio.Customers.Domain;

namespace Portfolio.Customers.Application;

/// <summary>Projects the aggregate onto its read model.</summary>
internal static class CustomerProfileMapping
{
    /// <summary>Builds the view of <paramref name="profile"/>, with contact data masked (BR-CUS-009).</summary>
    /// <param name="profile">Customer profile.</param>
    public static CustomerProfileView ToView(this CustomerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CustomerProfileView(
            profile.Id,
            profile.FullName?.Value,
            profile.Email.Masked(),
            profile.Phone?.Masked(),
            profile.Locale.Value,
            profile.TimeZone.Value,
            profile.Version
        );
    }
}
