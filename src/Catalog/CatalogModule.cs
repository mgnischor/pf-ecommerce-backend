using Portfolio.Catalog.Application;
using Portfolio.Catalog.Domain;
using Portfolio.Catalog.Infrastructure;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Catalog;

/// <summary>Composition of the Catalog context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class CatalogModule
{
    /// <summary>Registers the Catalog database, repository, and use cases. Needs <c>AddPostgres</c> first.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="environment">Host environment.</param>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddModuleDbContext<CatalogDbContext>(CatalogDbContext.SchemaName, environment);
        services.AddScoped<IProductRepository, EfProductRepository>();

        services.AddUseCase<CatalogDbContext, CreateProductHandler>();
        services.AddUseCase<CatalogDbContext, ChangeProductPriceHandler>();
        services.AddUseCase<CatalogDbContext, ActivateProductHandler>();
        services.AddUseCase<CatalogDbContext, DiscontinueProductHandler>();

        return services;
    }
}
