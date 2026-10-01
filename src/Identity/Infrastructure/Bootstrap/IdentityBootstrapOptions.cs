namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Accounts created at startup when they do not exist yet (<c>Identity:Bootstrap</c>). This is how the first
/// administrator and developer come into being, since no endpoint can create an account above the caller's own
/// level. Passwords are secrets: inject them from the secrets manager or <c>dotnet user-secrets</c>, never commit them.
/// </summary>
internal sealed class IdentityBootstrapOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Identity:Bootstrap";

    /// <summary>Accounts to ensure.</summary>
    public List<BootstrapAccount> Accounts { get; set; } = [];
}
