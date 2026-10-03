using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Domain;

/// <summary>
/// Stable errors of the Customers rules (docs/business-rules/customers.md).
/// Each error carries its code, field, business rule ID, and message parameters; text is localized at the API boundary.
/// </summary>
internal static class CustomerErrors
{
    /// <summary>BR-CUS-006: a profile cannot be created for an account without an identifier.</summary>
    public static Error AccountInvalid => Error.Validation("CUSTOMER_ACCOUNT_INVALID", "accountId", "BR-CUS-006");

    /// <summary>BR-CUS-001: the full name was sent empty; it can be changed but not removed.</summary>
    public static Error FullNameRequired => Error.Validation("CUSTOMER_FULL_NAME_REQUIRED", "fullName", "BR-CUS-001");

    /// <summary>BR-CUS-001: the full name is outside the allowed length.</summary>
    public static Error FullNameLength =>
        Error.Validation(
            "CUSTOMER_FULL_NAME_LENGTH",
            "fullName",
            "BR-CUS-001",
            ErrorParameters.Of(("min", FullName.MinLength), ("max", FullName.MaxLength))
        );

    /// <summary>BR-CUS-001: the full name contains control characters or no letter at all.</summary>
    public static Error FullNameInvalid => Error.Validation("CUSTOMER_FULL_NAME_INVALID", "fullName", "BR-CUS-001");

    /// <summary>BR-CUS-002: the contact e-mail is missing or not a valid address.</summary>
    public static Error EmailInvalid => Error.Validation("CUSTOMER_EMAIL_INVALID", "email", "BR-CUS-002");

    /// <summary>BR-CUS-003: the phone is not an E.164 number (<c>+</c> and 8 to 15 digits, not starting with 0).</summary>
    public static Error PhoneInvalid => Error.Validation("CUSTOMER_PHONE_INVALID", "phone", "BR-CUS-003");

    /// <summary>BR-CUS-004: the locale was sent empty; it can be changed but not removed.</summary>
    public static Error LocaleRequired => Error.Validation("CUSTOMER_LOCALE_REQUIRED", "locale", "BR-CUS-004");

    /// <summary>BR-CUS-004: the locale is not one of the supported ones.</summary>
    public static Error LocaleUnsupported =>
        Error.Validation(
            "CUSTOMER_LOCALE_UNSUPPORTED",
            "locale",
            "BR-CUS-004",
            ErrorParameters.Of(("supported", string.Join(", ", CustomerLocale.Supported)))
        );

    /// <summary>BR-CUS-005: the time zone was sent empty; it can be changed but not removed.</summary>
    public static Error TimeZoneRequired => Error.Validation("CUSTOMER_TIME_ZONE_REQUIRED", "timeZone", "BR-CUS-005");

    /// <summary>BR-CUS-005: the time zone is not an IANA identifier such as <c>America/Sao_Paulo</c>.</summary>
    public static Error TimeZoneInvalid => Error.Validation("CUSTOMER_TIME_ZONE_INVALID", "timeZone", "BR-CUS-005");

    /// <summary>BR-CUS-007: the account has no customer profile (staff accounts never have one) or it is not created yet.</summary>
    public static Error ProfileNotFound => Error.NotFound("CUSTOMER_PROFILE_NOT_FOUND");

    /// <summary>BR-CUS-008: the <c>If-Match</c> version is stale or malformed.</summary>
    public static Error VersionMismatch => Error.PreconditionFailed("CUSTOMER_VERSION_MISMATCH");
}
