using Portfolio.SharedKernel.Infrastructure;
using Portfolio.Shipping.Application;
using Portfolio.Shipping.Domain;
using Portfolio.Shipping.Infrastructure;

namespace Portfolio.Shipping;

/// <summary>Composition of the Shipping context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class ShippingModule
{
    /// <summary>Registers the Shipping database, repositories, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddShippingModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<ShippingDbContext>(ShippingDbContext.SchemaName, environment);
        services.AddScoped<IShipmentRepository, EfShipmentRepository>();
        services.AddScoped<IOrderReferenceRepository, EfOrderReferenceRepository>();
        services.AddSingleton<IDomainEventSubscriber, ShippingMetricsSubscriber>();

        services.AddUseCase<ShippingDbContext, DispatchShipmentHandler>();
        services.AddUseCase<ShippingDbContext, ConcludeShipmentHandler>();
        services.AddScoped<ListShipmentsHandler>();

        // The consumer is always known; it runs only where RabbitMq:ConsumersEnabled is true (the worker role).
        services.AddConsumerUseCase<ShippingDbContext, SyncOrderHandler>();
        services.AddSingleton<IMessageConsumer, OrderEventsConsumer>();

        return services;
    }
}
