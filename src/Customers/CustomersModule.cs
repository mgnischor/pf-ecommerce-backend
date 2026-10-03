using Portfolio.Customers.Application;
using Portfolio.Customers.Domain;
using Portfolio.Customers.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Customers;

/// <summary>Composition of the Customers context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class CustomersModule
{
    /// <summary>Registers the Customers database, repository, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<CustomersDbContext>(CustomersDbContext.SchemaName, environment);
        services.AddScoped<ICustomerProfileRepository, EfCustomerProfileRepository>();

        services.AddUseCase<CustomersDbContext, UpdateCustomerProfileHandler>();
        services.AddScoped<GetCustomerProfileHandler>();

        // The consumer is always known; it runs only where RabbitMq:ConsumersEnabled is true (the worker role).
        services.AddConsumerUseCase<CustomersDbContext, CreateCustomerProfileOnRegistrationHandler>();
        services.AddSingleton<IMessageConsumer, CustomerRegisteredConsumer>();

        return services;
    }
}
