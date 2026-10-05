using Microsoft.EntityFrameworkCore;
using Portfolio.Cart.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Cart.Infrastructure;

/// <summary>
/// The Cart context's database (ai/DATABASE.md §5): schema <c>cart</c>, its own migration history and outbox/inbox
/// tables. It maps only Cart entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class CartDbContext(DbContextOptions<CartDbContext> options) : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "cart";

    /// <summary>Shoppers' carts.</summary>
    public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();

    /// <summary>The lines of the carts.</summary>
    public DbSet<ShoppingCartItem> ShoppingCartItems => Set<ShoppingCartItem>();

    /// <summary>The Cart's own view of the catalog products.</summary>
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
}
