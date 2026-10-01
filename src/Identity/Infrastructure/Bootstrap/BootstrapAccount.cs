namespace Portfolio.Identity.Infrastructure;

/// <summary>One account to ensure at startup.</summary>
internal sealed class BootstrapAccount
{
    /// <summary>Account e-mail.</summary>
    public string? Email { get; set; }

    /// <summary>Initial password. Secret.</summary>
    public string? Password { get; set; }

    /// <summary>Wire name of the access level, for example <c>administrator</c>.</summary>
    public string? AccessLevel { get; set; }
}
