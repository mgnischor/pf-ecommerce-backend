using Portfolio.Customers.Application;
using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Customers.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Application;

[Trait("Rule", "BR-CUS-008")]
public sealed class UpdateCustomerProfileHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeCustomerProfileRepository _profiles = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly UpdateCustomerProfileHandler _handler;

    public UpdateCustomerProfileHandlerTests()
    {
        _handler = new UpdateCustomerProfileHandler(_profiles, _unitOfWork, _clock);
        _profiles.Seed(CustomerProfiles.Complete(_clock));
    }

    private static UpdateCustomerProfileCommand Patch(
        int? version = AggregateRoot.InitialVersion,
        Change<string?> fullName = default,
        Change<string?> phone = default,
        Change<string?> locale = default,
        Change<string?> timeZone = default,
        Guid? account = null
    ) => new(account ?? CustomerProfiles.AccountId, version, fullName, phone, locale, timeZone);

    private Task<Result<CustomerProfileView>> HandleAsync(UpdateCustomerProfileCommand command) =>
        _handler.HandleAsync(command, TestContext.Current.CancellationToken);

    private CustomerProfile Stored => _profiles.Profiles.ShouldHaveSingleItem();

    [Fact]
    public async Task Should_change_the_members_sent_keep_the_others_and_persist_once()
    {
        var result = await HandleAsync(
            Patch(fullName: Change<string?>.To("  Ana   Maria Souza "), locale: Change<string?>.To("EN"))
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.FullName.ShouldBe("Ana Maria Souza");
        result.Value.Locale.ShouldBe("en");
        result.Value.MaskedPhone.ShouldBe("+*********4321");
        result.Value.TimeZone.ShouldBe("America/Sao_Paulo");
        result.Value.Version.ShouldBe(AggregateRoot.InitialVersion + 1);
        _profiles.UpdateCalls.ShouldBe(1);
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_remove_the_phone_when_it_is_sent_as_null()
    {
        var result = await HandleAsync(Patch(phone: Change<string?>.To(null)));

        result.Value.MaskedPhone.ShouldBeNull();
        Stored.Phone.ShouldBeNull();
        _unitOfWork.SaveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Should_replace_the_phone()
    {
        var result = await HandleAsync(Patch(phone: Change<string?>.To("+351912345678")));

        result.Value.MaskedPhone.ShouldBe("+********5678");
        Stored.Phone!.Value.ShouldBe("+351912345678");
    }

    [Fact]
    public async Task Should_answer_the_current_state_and_write_nothing_when_the_patch_changes_nothing()
    {
        var empty = await HandleAsync(Patch());
        var same = await HandleAsync(
            Patch(fullName: Change<string?>.To("Ana Souza"), locale: Change<string?>.To("pt-br"))
        );

        empty.Value.Version.ShouldBe(AggregateRoot.InitialVersion);
        same.Value.Version.ShouldBe(AggregateRoot.InitialVersion);
        _profiles.UpdateCalls.ShouldBe(0);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_stale_version_as_a_failed_precondition_and_write_nothing()
    {
        var result = await HandleAsync(Patch(version: 7, fullName: Change<string?>.To("Outro Nome")));

        var error = result.ShouldFail();
        error.Code.ShouldBe("CUSTOMER_VERSION_MISMATCH");
        error.Type.ShouldBe(ErrorType.PreconditionFailed);
        Stored.FullName!.Value.ShouldBe("Ana Souza");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_reject_a_malformed_if_match_the_same_way()
    {
        var result = await HandleAsync(Patch(version: null, fullName: Change<string?>.To("Outro Nome")));

        result.ShouldFail().Code.ShouldBe("CUSTOMER_VERSION_MISMATCH");
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_check_the_version_before_the_data_so_a_stale_client_learns_to_re_read_first()
    {
        var result = await HandleAsync(Patch(version: 7, locale: Change<string?>.To("klingon")));

        result.ShouldFail().Code.ShouldBe("CUSTOMER_VERSION_MISMATCH");
    }

    [Fact]
    public async Task Should_answer_not_found_for_an_account_without_a_profile()
    {
        var result = await HandleAsync(Patch(account: Guid.CreateVersion7(), fullName: Change<string?>.To("Ana")));

        var error = result.ShouldFail();
        error.Code.ShouldBe("CUSTOMER_PROFILE_NOT_FOUND");
        error.Type.ShouldBe(ErrorType.NotFound);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("fullName", "A", "CUSTOMER_FULL_NAME_LENGTH")]
    [InlineData("fullName", "1234", "CUSTOMER_FULL_NAME_INVALID")]
    [InlineData("fullName", null, "CUSTOMER_FULL_NAME_REQUIRED")]
    [InlineData("fullName", "  ", "CUSTOMER_FULL_NAME_REQUIRED")]
    [InlineData("phone", "11987654321", "CUSTOMER_PHONE_INVALID")]
    [InlineData("phone", "+55 11 98765-4321", "CUSTOMER_PHONE_INVALID")]
    [InlineData("locale", "fr-FR", "CUSTOMER_LOCALE_UNSUPPORTED")]
    [InlineData("locale", null, "CUSTOMER_LOCALE_REQUIRED")]
    [InlineData("timeZone", "Mars/Olympus", "CUSTOMER_TIME_ZONE_INVALID")]
    [InlineData("timeZone", null, "CUSTOMER_TIME_ZONE_REQUIRED")]
    public async Task Should_reject_an_invalid_member_with_its_rule_and_persist_nothing(
        string field,
        string? value,
        string expectedCode
    )
    {
        var change = Change<string?>.To(value);
        var command = field switch
        {
            "fullName" => Patch(fullName: change),
            "phone" => Patch(phone: change),
            "locale" => Patch(locale: change),
            _ => Patch(timeZone: change),
        };

        var result = await HandleAsync(command);

        var error = result.ShouldFail();
        error.Code.ShouldBe(expectedCode);
        error.Type.ShouldBe(ErrorType.Validation);
        error.Field.ShouldBe(field);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Should_apply_nothing_when_one_member_of_the_patch_is_invalid()
    {
        var result = await HandleAsync(
            Patch(fullName: Change<string?>.To("Nome Valido"), locale: Change<string?>.To("klingon"))
        );

        result.ShouldFail().Code.ShouldBe("CUSTOMER_LOCALE_UNSUPPORTED");
        Stored.FullName!.Value.ShouldBe("Ana Souza");
        Stored.Version.ShouldBe(AggregateRoot.InitialVersion);
        _unitOfWork.SaveCalls.ShouldBe(0);
    }
}
