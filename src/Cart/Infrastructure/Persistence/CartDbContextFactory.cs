using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Cart.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Cart context without running the host.</summary>
internal sealed class CartDbContextFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    /// <inheritdoc />
    public CartDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<CartDbContext>(CartDbContext.SchemaName));
}
