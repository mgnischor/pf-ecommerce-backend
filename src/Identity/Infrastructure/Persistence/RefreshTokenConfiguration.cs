using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Identity.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>Mapping of <c>identity.refresh_tokens</c> (BR-IDN-005). Only the HMAC of a token is stored, never the token.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>Longest stored Base64url HMAC-SHA3-512 (64 bytes encode to 86 characters).</summary>
    private const int MaxTokenHashLength = 128;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "refresh_tokens",
            table => table.HasCheckConstraint("ck_refresh_tokens_expiry", "expires_at <= family_expires_at")
        );

        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.FamilyId).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(MaxTokenHashLength).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();
        builder.Property(token => token.FamilyExpiresAt).IsRequired();
        builder.Property(token => token.UsedAt);
        builder.Property(token => token.RevokedAt);

        // A hash identifies one token for ever, even after the row is logically deleted.
        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(token => token.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasIndex(token => token.UserId).HasDatabaseName("ix_refresh_tokens_user_id");

        // Same schema, so a real foreign key is allowed (ai/DATABASE.md §3.1); the domain keeps only the id.
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
