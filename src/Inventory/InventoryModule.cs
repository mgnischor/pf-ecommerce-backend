using Portfolio.Inventory.Application;
using Portfolio.Inventory.Domain;
using Portfolio.Inventory.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Inventory;

/// <summary>Composition of the Inventory context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class InventoryModule
{
    /// <summary>Registers the Inventory database, repository, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<InventoryDbContext>(InventoryDbContext.SchemaName, environment);
        services.AddScoped<IInventoryItemRepository, EfInventoryItemRepository>();
        services.AddSingleton<IDomainEventSubscriber, InventoryMetricsSubscriber>();

        services.AddUseCase<InventoryDbContext, OpenInventoryItemHandler>();
        services.AddUseCase<InventoryDbContext, AdjustStockHandler>();
        services.AddScoped<GetInventoryItemHandler>();

        // The consumer is always known; it runs only where RabbitMq:ConsumersEnabled is true (the worker role).
        services.AddConsumerUseCase<InventoryDbContext, OpenInventoryItemOnProductCreatedHandler>();
        services.AddSingleton<IMessageConsumer, ProductCreatedConsumer>();

        return services;
    }
}
