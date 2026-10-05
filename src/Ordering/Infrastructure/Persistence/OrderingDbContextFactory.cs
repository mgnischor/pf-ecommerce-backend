using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Ordering.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Ordering context without running the host.</summary>
internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    /// <inheritdoc />
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<OrderingDbContext>(OrderingDbContext.SchemaName));
}
