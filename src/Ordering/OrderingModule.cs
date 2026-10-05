using Portfolio.Ordering.Application;
using Portfolio.Ordering.Domain;
using Portfolio.Ordering.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Ordering;

/// <summary>Composition of the Ordering context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class OrderingModule
{
    /// <summary>Registers the Ordering database, repository, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<OrderingDbContext>(OrderingDbContext.SchemaName, environment);
        services.AddScoped<IOrderRepository, EfOrderRepository>();
        services.AddSingleton<IDomainEventSubscriber, OrderingMetricsSubscriber>();

        services.AddUseCase<OrderingDbContext, PlaceOrderHandler>();
        services.AddUseCase<OrderingDbContext, CancelOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ListOrdersHandler>();

        // The consumers are always known; they run only where RabbitMq:ConsumersEnabled is true (the worker role).
        services.AddConsumerUseCase<OrderingDbContext, AdvanceOrderHandler>();
        services.AddSingleton<IMessageConsumer, ShipmentDispatchedConsumer>();
        services.AddSingleton<IMessageConsumer, ShipmentDeliveredConsumer>();

        return services;
    }
}
