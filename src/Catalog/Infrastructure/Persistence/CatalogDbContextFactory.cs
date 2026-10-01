using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Catalog.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Catalog context without running the host.</summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    /// <inheritdoc />
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<CatalogDbContext>(CatalogDbContext.SchemaName));
}
