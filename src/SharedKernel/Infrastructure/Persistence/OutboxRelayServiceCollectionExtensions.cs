using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Registration of the relay.</summary>
internal static class OutboxRelayServiceCollectionExtensions
{
    /// <summary>
    /// Runs the relay of one context with the given publisher. Not part of the default composition: it needs a
    /// publisher, which the messaging infrastructure provides. Without one, events simply accumulate in the outbox.
    /// </summary>
    /// <typeparam name="TContext">The context whose outbox is relayed.</typeparam>
    /// <typeparam name="TPublisher">The broker publisher.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddOutboxRelay<TContext, TPublisher>(
        this IServiceCollection services,
        IConfiguration configuration
    )
        where TContext : ModuleDbContext
        where TPublisher : class, IOutboxPublisher
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OutboxRelayOptions>().Bind(configuration.GetSection(OutboxRelayOptions.SectionName));
        services.TryAddSingleton<IOutboxPublisher, TPublisher>();
        return services.AddHostedService<OutboxRelay<TContext>>();
    }
}
