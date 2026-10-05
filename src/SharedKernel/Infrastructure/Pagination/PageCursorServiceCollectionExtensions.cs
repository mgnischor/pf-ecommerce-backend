using Microsoft.Extensions.Options;
using Portfolio.SharedKernel.Application;

namespace Portfolio.SharedKernel.Infrastructure;

/// <summary>Composition of the cursor signing (ai/API_CONTRACTS.md §6).</summary>
internal static class PageCursorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cursor codec. Outside Development the host refuses to start without
    /// <c>Pagination:CursorKey</c> (fail closed, ai/SECURITY.md §5), because the codec is built at startup.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment; only Development may use an ephemeral key.</param>
    public static IServiceCollection AddPageCursors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var allowEphemeralKey = environment.IsDevelopment();

        services.AddOptions<PageCursorOptions>().Bind(configuration.GetSection(PageCursorOptions.SectionName));
        services.AddSingleton<IPageCursorCodec>(provider => new HmacPageCursorCodec(
            provider.GetRequiredService<IOptions<PageCursorOptions>>(),
            allowEphemeralKey,
            provider.GetRequiredService<ILogger<HmacPageCursorCodec>>()
        ));
        services.AddHostedService<PageCursorStartupCheck>();

        return services;
    }
}
