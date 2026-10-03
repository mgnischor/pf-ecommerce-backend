using Portfolio.Customers.Application;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Customers.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Application;

[Trait("Rule", "BR-CUS-006")]
public sealed class CreateCustomerProfileOnRegistrationHandlerTests
{
    private static readonly Guid Message = Guid.Parse("0199f3a2-7c10-7d3e-8a51-2b9d4c6e1f31");

    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeCustomerProfileRepository _profiles = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeInbox _inbox = new();
    private readonly CreateCustomerProfileOnRegistrationHandler _handler;

    public CreateCustomerProfileOnRegistrationHandlerTests()
    {
        _handler = new CreateCustomerProfileOnRegistrationHandler(_profiles, _unitOfWork, _inbox, _clock);
    }

    private static CreateCustomerProfileOnRegistrationCommand Event(
        Guid? message = null,
        Guid? account = null,
        string? email = "ana.souza@example.com",
        string? fullName = "Ana Souza",
        string? phone = "+5511987654321",
        string? locale = "en",
        string? timeZone = "Europe/Lisbon"
    ) => new(message ?? Message, account ?? CustomerProfiles.AccountId, email, fullName, phone, locale, timeZone);

    private Task<Result<ProfileCreation>> HandleAsync(CreateCustomerProfileOnRegistrationCommand command) =>
        _handler.HandleAsync(command, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_create_the_profile_of_the_account_from_what_the_customer_typed()
    {
        var result = await HandleAsync(Event());

        result.IsSuccess.ShouldBeTrue();
        result.Value.Handled.ShouldBeTrue();
        result.Value.IgnoredFields.ShouldBeEmpty();
        var profile = _profiles.Profiles.ShouldHaveSingleItem();
        profile.Id.ShouldBe(CustomerProfiles.AccountId);
        profile.Email.Value.ShouldBe("ana.souza@example.com");
        profile.FullName!.Value.ShouldBe("Ana Souza");
        profile.Phone!.Value.ShouldBe("+5511987654321");
        profile.Locale.Value.ShouldBe("en");
        profile.TimeZone.Value.ShouldBe("Europe/Lisbon");
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_create_a_profile_with_the_defaults_when_the_customer_typed_nothing()
    {
        var result = await HandleAsync(Event(fullName: null, phone: null, locale: null, timeZone: null));

        result.Value.Handled.ShouldBeTrue();
        result.Value.IgnoredFields.ShouldBeEmpty();
        var profile = _profiles.Profiles.ShouldHaveSingleItem();
        profile.FullName.ShouldBeNull();
        profile.Phone.ShouldBeNull();
        profile.Locale.Value.ShouldBe("pt-BR");
        profile.TimeZone.Value.ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    public async Task Should_not_lose_the_profile_over_optional_data_that_breaks_a_rule()
    {
        var result = await HandleAsync(
            Event(fullName: "1234", phone: "not-a-phone", locale: "fr-FR", timeZone: "Mars/Olympus")
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Handled.ShouldBeTrue();
        result.Value.IgnoredFields.ShouldBe(["fullName", "phone", "locale", "timeZone"]);
        var profile = _profiles.Profiles.ShouldHaveSingleItem();
        profile.FullName.ShouldBeNull();
        profile.Phone.ShouldBeNull();
        profile.Locale.Value.ShouldBe("pt-BR");
        profile.TimeZone.Value.ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    public async Task Should_keep_the_valid_fields_and_report_only_the_broken_ones_by_name()
    {
        var result = await HandleAsync(Event(phone: "+55 11 98765-4321"));

        result.Value.IgnoredFields.ShouldBe(["phone"]);
        _profiles.Profiles.ShouldHaveSingleItem().FullName!.Value.ShouldBe("Ana Souza");
    }

    [Fact]
    public async Task Should_do_nothing_when_the_same_message_is_delivered_again()
    {
        await HandleAsync(Event());

        var replay = await HandleAsync(Event());

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Handled.ShouldBeFalse();
        _profiles.Profiles.Count.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_leave_an_existing_profile_untouched_but_still_record_the_message_as_handled()
    {
        _profiles.Seed(CustomerProfiles.Complete(_clock));

        var result = await HandleAsync(Event(message: Guid.CreateVersion7(), fullName: "Outro Nome"));

        result.Value.Handled.ShouldBeTrue();
        var profile = _profiles.Profiles.ShouldHaveSingleItem();
        profile.FullName!.Value.ShouldBe("Ana Souza");
        profile.Version.ShouldBe(AggregateRoot.InitialVersion);
        _inbox.Begun.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Should_fail_for_an_event_without_a_usable_email_and_create_nothing(string? email)
    {
        var result = await HandleAsync(Event(email: email));

        result.ShouldFail().Code.ShouldBe("CUSTOMER_EMAIL_INVALID");
        _profiles.Profiles.ShouldBeEmpty();
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_fail_for_an_event_without_an_account_identifier()
    {
        var result = await HandleAsync(Event(account: Guid.Empty));

        result.ShouldFail().Code.ShouldBe("CUSTOMER_ACCOUNT_INVALID");
        _profiles.Profiles.ShouldBeEmpty();
    }
}
