using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Customers.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Customers context without running the host.</summary>
internal sealed class CustomersDbContextFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    /// <inheritdoc />
    public CustomersDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<CustomersDbContext>(CustomersDbContext.SchemaName));
}
