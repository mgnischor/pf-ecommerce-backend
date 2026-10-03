using Portfolio.Customers.Application;
using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Domain;
using Portfolio.UnitTests.Customers.Domain;
using Portfolio.UnitTests.Support;

namespace Portfolio.UnitTests.Customers.Application;

[Trait("Rule", "BR-CUS-007")]
public sealed class GetCustomerProfileHandlerTests
{
    private readonly FakeTimeProvider _clock = TestClock.Create();
    private readonly FakeCustomerProfileRepository _profiles = new();
    private readonly GetCustomerProfileHandler _handler;

    public GetCustomerProfileHandlerTests()
    {
        _handler = new GetCustomerProfileHandler(_profiles);
    }

    [Fact]
    [Trait("Rule", "BR-CUS-009")]
    public async Task Should_return_the_callers_profile_with_the_contact_data_masked()
    {
        _profiles.Seed(CustomerProfiles.Complete(_clock));

        var result = await _handler.HandleAsync(
            new GetCustomerProfileQuery(CustomerProfiles.AccountId),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(
            new CustomerProfileView(
                CustomerProfiles.AccountId,
                "Ana Souza",
                "a***@example.com",
                "+*********4321",
                "pt-BR",
                "America/Sao_Paulo",
                AggregateRoot.InitialVersion
            )
        );
    }

    [Fact]
    public async Task Should_show_null_for_the_name_and_the_phone_the_customer_has_not_given()
    {
        _profiles.Seed(
            CustomerProfile.Create(CustomerProfiles.AccountId, CustomerProfiles.Email(), null, null, null, null, _clock)
        );

        var result = await _handler.HandleAsync(
            new GetCustomerProfileQuery(CustomerProfiles.AccountId),
            TestContext.Current.CancellationToken
        );

        result.Value.FullName.ShouldBeNull();
        result.Value.MaskedPhone.ShouldBeNull();
        result.Value.Locale.ShouldBe("pt-BR");
        result.Value.TimeZone.ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    public async Task Should_never_expose_the_clear_email_or_phone_in_the_view()
    {
        _profiles.Seed(CustomerProfiles.Complete(_clock));

        var view = (
            await _handler.HandleAsync(
                new GetCustomerProfileQuery(CustomerProfiles.AccountId),
                TestContext.Current.CancellationToken
            )
        ).Value;

        view.ToString().ShouldNotContain("ana.souza");
        view.ToString().ShouldNotContain("1198765");
    }

    [Fact]
    public async Task Should_answer_not_found_for_an_account_without_a_profile_such_as_a_staff_account()
    {
        _profiles.Seed(CustomerProfiles.Complete(_clock));
        var staffAccount = Guid.CreateVersion7();

        var result = await _handler.HandleAsync(
            new GetCustomerProfileQuery(staffAccount),
            TestContext.Current.CancellationToken
        );

        var error = result.ShouldFail();
        error.Code.ShouldBe("CUSTOMER_PROFILE_NOT_FOUND");
        error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Should_not_reveal_another_customers_profile_because_only_the_callers_account_is_looked_up()
    {
        var someoneElse = Guid.CreateVersion7();
        _profiles.Seed(CustomerProfiles.Complete(_clock, someoneElse));

        var result = await _handler.HandleAsync(
            new GetCustomerProfileQuery(CustomerProfiles.AccountId),
            TestContext.Current.CancellationToken
        );

        result.ShouldFail().Code.ShouldBe("CUSTOMER_PROFILE_NOT_FOUND");
    }
}
