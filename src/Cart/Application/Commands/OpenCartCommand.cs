namespace Portfolio.Cart.Application;

/// <summary>Request to open a cart for a customer (BR-CRT-001).</summary>
/// <param name="CustomerId">The authenticated account.</param>
/// <param name="Currency">ISO 4217 currency the cart is priced in.</param>
internal sealed record OpenCartCommand(Guid CustomerId, string? Currency);
