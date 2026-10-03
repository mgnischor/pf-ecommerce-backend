using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Domain;

/// <summary>Builds valid profiles with sensible defaults; tests override only what they assert on (ai/TESTS.md §10).</summary>
internal static class CustomerProfiles
{
    public static readonly Guid AccountId = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f21");

    public static ContactEmail Email(string value = "ana.souza@example.com") => ContactEmail.Create(value).Value;

    public static FullName Name(string value = "Ana Souza") => FullName.Create(value).Value;

    public static PhoneNumber Phone(string value = "+5511987654321") => PhoneNumber.Create(value).Value;

    public static CustomerLocale Locale(string value = "en") => CustomerLocale.Create(value).Value;

    public static CustomerTimeZone Zone(string value = "Europe/Lisbon") => CustomerTimeZone.Create(value).Value;

    /// <summary>A profile with every optional member given.</summary>
    public static CustomerProfile Complete(TimeProvider clock, Guid? accountId = null) =>
        CustomerProfile.Create(
            accountId ?? AccountId,
            Email(),
            Name(),
            Phone(),
            Locale("pt-BR"),
            Zone("America/Sao_Paulo"),
            clock
        );
}

[Trait("Rule", "BR-CUS-006")]
public sealed class CustomerProfileCreationTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();

    [Fact]
    public void Should_take_the_account_identifier_as_its_own_and_start_at_the_initial_version()
    {
        var profile = CustomerProfiles.Complete(_clock);

        profile.Id.ShouldBe(CustomerProfiles.AccountId);
        profile.Version.ShouldBe(AggregateRoot.InitialVersion);
        profile.CreatedAt.ShouldBe(_clock.GetUtcNow());
        profile.UpdatedAt.ShouldBe(_clock.GetUtcNow());
        profile.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void Should_default_the_locale_and_the_time_zone_and_leave_the_name_and_phone_empty_when_none_were_given()
    {
        var profile = CustomerProfile.Create(
            CustomerProfiles.AccountId,
            CustomerProfiles.Email(),
            null,
            null,
            null,
            null,
            _clock
        );

        profile.FullName.ShouldBeNull();
        profile.Phone.ShouldBeNull();
        profile.Locale.ShouldBe(CustomerLocale.Default);
        profile.TimeZone.ShouldBe(CustomerTimeZone.Default);
    }

    [Fact]
    public void Should_reject_an_account_without_an_identifier()
    {
        Should.Throw<ArgumentException>(() =>
            CustomerProfile.Create(Guid.Empty, CustomerProfiles.Email(), null, null, null, null, _clock)
        );
    }

    [Fact]
    public void Should_raise_no_domain_event_because_the_profile_is_personal_data()
    {
        CustomerProfiles.Complete(_clock).DomainEvents.ShouldBeEmpty();
    }
}

[Trait("Rule", "BR-CUS-008")]
public sealed class CustomerProfileUpdateTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly CustomerProfile _profile;

    public CustomerProfileUpdateTests()
    {
        _profile = CustomerProfiles.Complete(_clock);
    }

    private bool Update(
        Change<FullName> fullName = default,
        Change<PhoneNumber> phone = default,
        Change<CustomerLocale> locale = default,
        Change<CustomerTimeZone> timeZone = default
    ) => _profile.Update(fullName, phone, locale, timeZone, _clock);

    [Fact]
    public void Should_change_only_the_members_that_were_sent()
    {
        _clock.Advance(TimeSpan.FromMinutes(5));

        var changed = Update(fullName: Change<FullName>.To(CustomerProfiles.Name("Ana Maria Souza")));

        changed.ShouldBeTrue();
        _profile.FullName!.Value.ShouldBe("Ana Maria Souza");
        _profile.Phone!.Value.ShouldBe("+5511987654321");
        _profile.Locale.Value.ShouldBe("pt-BR");
        _profile.TimeZone.Value.ShouldBe("America/Sao_Paulo");
        _profile.UpdatedAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public void Should_advance_the_version_once_even_when_several_members_change()
    {
        var changed = Update(
            Change<FullName>.To(CustomerProfiles.Name("Ana Maria")),
            Change<PhoneNumber>.To(CustomerProfiles.Phone("+351912345678")),
            Change<CustomerLocale>.To(CustomerProfiles.Locale("en")),
            Change<CustomerTimeZone>.To(CustomerProfiles.Zone("Europe/Lisbon"))
        );

        changed.ShouldBeTrue();
        _profile.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    public void Should_remove_the_phone_when_the_client_sent_it_as_null()
    {
        var changed = Update(phone: Change<PhoneNumber>.To(null));

        changed.ShouldBeTrue();
        _profile.Phone.ShouldBeNull();
        _profile.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
    }

    [Fact]
    public void Should_leave_the_version_and_the_update_time_alone_when_nothing_was_sent()
    {
        _clock.Advance(TimeSpan.FromMinutes(5));

        var changed = Update();

        changed.ShouldBeFalse();
        _profile.Version.ShouldBe(AggregateRoot.InitialVersion);
        _profile.UpdatedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_leave_the_version_alone_when_the_values_sent_are_the_ones_it_already_has()
    {
        _clock.Advance(TimeSpan.FromMinutes(5));

        var changed = Update(
            Change<FullName>.To(CustomerProfiles.Name()),
            Change<PhoneNumber>.To(CustomerProfiles.Phone()),
            Change<CustomerLocale>.To(CustomerProfiles.Locale("pt-BR")),
            Change<CustomerTimeZone>.To(CustomerProfiles.Zone("America/Sao_Paulo"))
        );

        changed.ShouldBeFalse();
        _profile.Version.ShouldBe(AggregateRoot.InitialVersion);
        _profile.UpdatedAt.ShouldBe(TestClock.Start);
    }

    [Fact]
    public void Should_not_touch_the_email_which_belongs_to_identity()
    {
        Update(fullName: Change<FullName>.To(CustomerProfiles.Name("Outra Pessoa")));

        _profile.Email.Value.ShouldBe("ana.souza@example.com");
    }

    [Fact]
    public void Should_set_the_phone_of_a_profile_that_had_none()
    {
        var profile = CustomerProfile.Create(
            CustomerProfiles.AccountId,
            CustomerProfiles.Email(),
            null,
            null,
            null,
            null,
            _clock
        );

        var changed = profile.Update(
            default,
            Change<PhoneNumber>.To(CustomerProfiles.Phone()),
            default,
            default,
            _clock
        );

        changed.ShouldBeTrue();
        profile.Phone.ShouldBe(CustomerProfiles.Phone());
    }
}
