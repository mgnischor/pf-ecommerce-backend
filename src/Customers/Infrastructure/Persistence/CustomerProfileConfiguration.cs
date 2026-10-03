using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portfolio.Customers.Domain;

namespace Portfolio.Customers.Infrastructure;

/// <summary>
/// Mapping of <c>customers.customer_profiles</c> (BR-CUS-001 to BR-CUS-006). Shared columns come from the entity
/// conventions; the key is the account's identifier, so a second profile for an account is impossible by construction.
/// </summary>
/// <remarks>
/// Data classification (ai/DATABASE.md §9): <c>full_name</c>, <c>contact_email</c> and <c>phone_number</c> are personal
/// data (PII). They are returned only masked or to their owner, never logged, and they are the fields an erasure
/// procedure anonymizes. <c>locale</c> and <c>time_zone</c> are preferences, not identifying.
/// </remarks>
internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "customer_profiles",
            table =>
            {
                // BR-CUS-003: the safety net under the domain rule, in the database (ai/DATABASE.md §3.3).
                table.HasCheckConstraint(
                    "ck_customer_profiles_phone_e164",
                    "phone_number IS NULL OR phone_number ~ '^\\+[1-9][0-9]{7,14}$'"
                );
            }
        );

        builder
            .Property(profile => profile.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(FullName.MaxLength)
            .HasConversion(
                name => name == null ? null : name.Value,
                value => value == null ? null : FullName.Create(value).Value
            );

        builder
            .Property(profile => profile.Email)
            .HasColumnName("contact_email")
            .HasMaxLength(ContactEmail.MaxLength)
            .HasConversion(email => email.Value, value => ContactEmail.Create(value).Value)
            .IsRequired();

        builder
            .Property(profile => profile.Phone)
            .HasColumnName("phone_number")
            .HasMaxLength(PhoneNumber.MaxDigits + 1)
            .HasConversion(
                phone => phone == null ? null : phone.Value,
                value => value == null ? null : PhoneNumber.Create(value).Value
            );

        builder
            .Property(profile => profile.Locale)
            .HasColumnName("locale")
            .HasMaxLength(16)
            .HasConversion(locale => locale.Value, value => CustomerLocale.Create(value).Value)
            .IsRequired();

        builder
            .Property(profile => profile.TimeZone)
            .HasColumnName("time_zone")
            .HasMaxLength(CustomerTimeZone.MaxLength)
            .HasConversion(zone => zone.Value, value => CustomerTimeZone.Create(value).Value)
            .IsRequired();
    }
}
