namespace Portfolio.Customers.Application;

/// <summary>How a <c>CustomerRegistered</c> message ended.</summary>
/// <param name="Handled"><c>true</c> when handled now; <c>false</c> when the inbox showed it was handled before.</param>
/// <param name="IgnoredFields">Optional fields that broke a rule and were left out (names only, never values).</param>
internal sealed record ProfileCreation(bool Handled, IReadOnlyList<string> IgnoredFields)
{
    /// <summary>The message was a redelivery: nothing was done.</summary>
    public static ProfileCreation Duplicate { get; } = new(Handled: false, []);
}
