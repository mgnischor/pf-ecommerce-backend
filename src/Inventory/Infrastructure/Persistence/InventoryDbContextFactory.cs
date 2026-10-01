using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Inventory.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Inventory context without running the host.</summary>
internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    /// <inheritdoc />
    public InventoryDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<InventoryDbContext>(InventoryDbContext.SchemaName));
}
