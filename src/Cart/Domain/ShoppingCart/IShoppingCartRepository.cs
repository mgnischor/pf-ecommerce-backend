using Portfolio.SharedKernel.Domain;

namespace Portfolio.Cart.Domain;

/// <summary>
/// Persistence abstraction for the <see cref="ShoppingCart"/> aggregate (with its lines).
/// Defined in the domain; implemented in Infrastructure.
/// </summary>
internal interface IShoppingCartRepository : IRepository<ShoppingCart>
{
    /// <summary>The customer's active cart, or <c>null</c> when they have none (BR-CRT-001).</summary>
    /// <param name="customerId">Owner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ShoppingCart?> FindActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}
