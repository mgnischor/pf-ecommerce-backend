using Microsoft.Extensions.Options;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Composition of the RabbitMQ access: the shared connection and a readiness check.</summary>
internal static class RabbitMqServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connection and the check. The connection string is the secret
    /// <c>ConnectionStrings:RabbitMQ</c>. This does not start any relay: roles that publish register one with
    /// <see cref="OutboxRelayServiceCollectionExtensions.AddOutboxRelay{TContext, TPublisher}"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnection>();

        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>(RabbitMqHealthCheck.Name, tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Runs the registered <see cref="IMessageConsumer"/>s in this process. Needs <see cref="AddRabbitMq"/>. Not part of
    /// the default composition: only the worker role consumes (ai/CONTAINERS.md §6.1).
    /// </summary>
    /// <param name="services">Service collection.</param>
    public static IServiceCollection AddRabbitMqConsumers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHostedService<RabbitMqConsumerService>();
    }
}
