namespace Portfolio.Cart.Domain;

/// <summary>
/// Lifecycle status of a <see cref="ShoppingCart"/> (BR-CRT-004).
/// Valid transitions: Active → CheckedOut, Active → Expired. Both are terminal.
/// </summary>
internal enum CartStatus
{
    /// <summary>Open for changes.</summary>
    Active = 0,

    /// <summary>Converted into a checkout; the cart no longer changes.</summary>
    CheckedOut = 1,

    /// <summary>Expired after inactivity; the cart no longer changes.</summary>
    Expired = 2,
}
