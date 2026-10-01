using Microsoft.EntityFrameworkCore.Design;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity.Infrastructure;

/// <summary>Lets <c>dotnet ef</c> build the Identity context without running the host.</summary>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(DesignTimeOptions.Create<IdentityDbContext>(IdentityDbContext.SchemaName));
}
