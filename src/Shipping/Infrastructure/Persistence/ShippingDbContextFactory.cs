using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Shipping.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Shipping context without running the host.</summary>
internal sealed class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    /// <inheritdoc />
    public ShippingDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<ShippingDbContext>(ShippingDbContext.SchemaName));
}
