using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Identity.Domain;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Identity.Infrastructure;

/// <summary>
/// Mapping of <c>identity.users</c> (BR-IDN-001, BR-IDN-003, BR-IDN-004, BR-IDN-006). Shared columns come from the
/// entity conventions.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Longest stored Argon2id PHC string.</summary>
    private const int MaxPasswordHashLength = 512;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var levels = string.Join(", ", Enum.GetNames<AccessLevel>().Select(name => $"'{name}'"));
        var statuses = string.Join(", ", Enum.GetNames<UserStatus>().Select(name => $"'{name}'"));

        builder.ToTable(
            "users",
            table =>
            {
                table.HasCheckConstraint("ck_users_access_level", $"access_level IN ({levels})");
                table.HasCheckConstraint("ck_users_status", $"status IN ({statuses})");
                table.HasCheckConstraint("ck_users_token_version", "token_version >= 1");
                table.HasCheckConstraint("ck_users_failed_sign_ins", "failed_sign_ins >= 0");
            }
        );

        builder
            .Property(user => user.Email)
            .HasMaxLength(EmailAddress.MaxLength)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value).Value)
            .IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(MaxPasswordHashLength).IsRequired();
        builder.Property(user => user.AccessLevel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(user => user.TokenVersion).IsRequired();
        builder.Property(user => user.FailedSignIns).IsRequired();
        builder.Property(user => user.LockedUntil);
        builder.Property(user => user.LastSignInAt);

        // BR-IDN-001: one active account per e-mail; a logically deleted account releases its e-mail.
        builder
            .HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_users_email_active");
    }
}
