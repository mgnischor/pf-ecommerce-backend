namespace Portfolio.Identity.Application;

/// <summary>Lifetimes of the issued tokens (ai/SECURITY.md §7.2: access ≤ 15 min, refresh ≤ 7 days, family ≤ 30 days).</summary>
/// <param name="Access">Lifetime of an access token.</param>
/// <param name="Refresh">Lifetime of one refresh token.</param>
/// <param name="RefreshFamily">Absolute lifetime of a sign-in session, however often it is refreshed.</param>
internal sealed record TokenLifetimes(TimeSpan Access, TimeSpan Refresh, TimeSpan RefreshFamily);
