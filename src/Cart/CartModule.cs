using Portfolio.Cart.Application;
using Portfolio.Cart.Domain;
using Portfolio.Cart.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Cart;

/// <summary>Composition of the Cart context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class CartModule
{
    /// <summary>Registers the Cart database, repositories, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddCartModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<CartDbContext>(CartDbContext.SchemaName, environment);
        services.AddScoped<IShoppingCartRepository, EfShoppingCartRepository>();
        services.AddScoped<ICatalogProductRepository, EfCatalogProductRepository>();

        services.AddUseCase<CartDbContext, OpenCartHandler>();
        services.AddUseCase<CartDbContext, AddCartItemHandler>();
        services.AddUseCase<CartDbContext, RemoveCartItemHandler>();
        services.AddScoped<GetCartHandler>();

        // The consumer is always known; it runs only where RabbitMq:ConsumersEnabled is true (the worker role).
        services.AddConsumerUseCase<CartDbContext, SyncCatalogProductHandler>();
        services.AddSingleton<IMessageConsumer, CatalogProductsConsumer>();

        return services;
    }
}
