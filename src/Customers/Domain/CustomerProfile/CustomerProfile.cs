using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// What the platform knows about a customer as a person, aggregate root of the Customers context. The profile of an
/// account has the account's identifier (BR-CUS-006), so "the authenticated customer" needs no lookup and no other
/// identifier is ever exposed (BR-CUS-007). Enforces BR-CUS-001 to BR-CUS-005 through its value objects and BR-CUS-008
/// (the version moves once per effective change).
/// </summary>
internal sealed class CustomerProfile : AggregateRoot
{
    /// <summary>
    /// Name as entered by the customer; <c>null</c> until they give one, because registration does not require it
    /// (BR-CUS-001). Once given it can be changed but not removed.
    /// </summary>
    public FullName? FullName { get; private set; }

    /// <summary>E-mail copied from the account; read-only here, changing it belongs to Identity (BR-CUS-002).</summary>
    public ContactEmail Email { get; private set; }

    /// <summary>Optional phone (BR-CUS-003).</summary>
    public PhoneNumber? Phone { get; private set; }

    /// <summary>Preferred locale (BR-CUS-004).</summary>
    public CustomerLocale Locale { get; private set; }

    /// <summary>IANA time zone (BR-CUS-005).</summary>
    public CustomerTimeZone TimeZone { get; private set; }

    /// <summary>EF Core constructor. Do not use in domain code.</summary>
    // Justification for CS8618 suppression: properties are populated by EF Core materialization.
#pragma warning disable CS8618
    private CustomerProfile() { }
#pragma warning restore CS8618

    private CustomerProfile(
        Guid accountId,
        ContactEmail email,
        FullName? fullName,
        PhoneNumber? phone,
        CustomerLocale locale,
        CustomerTimeZone timeZone,
        TimeProvider timeProvider
    )
        : base(accountId, timeProvider)
    {
        Email = email;
        FullName = fullName;
        Phone = phone;
        Locale = locale;
        TimeZone = timeZone;
    }

    /// <summary>Creates the profile of a customer account (BR-CUS-006).</summary>
    /// <param name="accountId">Identifier of the account; it becomes the identifier of the profile.</param>
    /// <param name="email">E-mail of the account.</param>
    /// <param name="fullName">Name, when the customer gave one.</param>
    /// <param name="phone">Phone, when the customer gave one.</param>
    /// <param name="locale">Preferred locale; <see cref="CustomerLocale.Default"/> when the customer gave none.</param>
    /// <param name="timeZone">Time zone; <see cref="CustomerTimeZone.Default"/> when the customer gave none.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    public static CustomerProfile Create(
        Guid accountId,
        ContactEmail email,
        FullName? fullName,
        PhoneNumber? phone,
        CustomerLocale? locale,
        CustomerTimeZone? timeZone,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new CustomerProfile(
            accountId,
            email,
            fullName,
            phone,
            locale ?? CustomerLocale.Default,
            timeZone ?? CustomerTimeZone.Default,
            timeProvider
        );
    }

    /// <summary>
    /// Applies a partial update (BR-CUS-008). A member the client did not send keeps its value; one it sent with the
    /// value it already has changes nothing. The version advances once, and only when something changed, so a client
    /// repeating a request does not invalidate the version it holds for no reason.
    /// </summary>
    /// <param name="fullName">New name.</param>
    /// <param name="phone">New phone; <c>null</c> removes it.</param>
    /// <param name="locale">New locale.</param>
    /// <param name="timeZone">New time zone.</param>
    /// <param name="timeProvider">Source of UTC time.</param>
    /// <returns><c>true</c> when the profile changed.</returns>
    public bool Update(
        Change<FullName> fullName,
        Change<PhoneNumber> phone,
        Change<CustomerLocale> locale,
        Change<CustomerTimeZone> timeZone,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        var changed = false;
        changed |= Apply(fullName, FullName, value => FullName = value);
        changed |= Apply(phone, Phone, value => Phone = value);
        changed |= Apply(locale, Locale, value => Locale = value!);
        changed |= Apply(timeZone, TimeZone, value => TimeZone = value!);

        if (changed)
        {
            MarkUpdated(timeProvider);
        }

        return changed;
    }

    private static bool Apply<T>(Change<T> change, T? current, Action<T?> assign)
        where T : class
    {
        if (!change.IsSet || EqualityComparer<T?>.Default.Equals(change.Value, current))
        {
            return false;
        }

        assign(change.Value);
        return true;
    }
}
