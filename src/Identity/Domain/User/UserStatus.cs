namespace Portfolio.Identity.Domain;

/// <summary>Lifecycle status of an account.</summary>
internal enum UserStatus
{
    /// <summary>The account can sign in.</summary>
    Active = 0,

    /// <summary>The account is disabled: it cannot sign in and every token it holds is revoked (BR-IDN-006).</summary>
    Deactivated = 1,
}
