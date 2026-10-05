namespace Portfolio.Cart.Application;

/// <summary>Lifecycle status of a cart as the API may show it (BR-CRT-004).</summary>
internal enum CartStatusView
{
    /// <summary>Open for changes.</summary>
    Active = 0,

    /// <summary>Converted into a checkout.</summary>
    CheckedOut = 1,

    /// <summary>Expired after inactivity.</summary>
    Expired = 2,
}
