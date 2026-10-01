namespace Portfolio.Identity.Application;

/// <summary>Request to exchange credentials for tokens.</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Account password. Never logged or echoed.</param>
internal sealed record SignInCommand(string? Email, string? Password);
