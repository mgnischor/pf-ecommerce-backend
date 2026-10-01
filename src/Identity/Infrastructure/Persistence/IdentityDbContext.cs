using Microsoft.EntityFrameworkCore;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// The Identity context's database (ai/DATABASE.md §5): schema <c>identity</c>, its own migration history and
/// outbox/inbox tables. It maps only Identity entities and never references another context's schema.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    /// <summary>The PostgreSQL schema the context owns.</summary>
    public const string SchemaName = "identity";

    /// <summary>Accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Refresh tokens of every session.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
}
