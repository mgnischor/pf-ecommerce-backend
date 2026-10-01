namespace Portfolio.Identity.Application;

/// <summary>Request to create a customer account (public access level).</summary>
/// <param name="Email">Account e-mail.</param>
/// <param name="Password">Chosen password. Never logged or echoed.</param>
internal sealed record RegisterCustomerCommand(string? Email, string? Password);
