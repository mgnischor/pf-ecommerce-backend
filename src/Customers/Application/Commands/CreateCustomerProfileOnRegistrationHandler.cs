using Portfolio.Customers.Domain;
using Portfolio.SharedKernel.Application;
using Portfolio.SharedKernel.Domain;

namespace Portfolio.Customers.Application;

/// <summary>
/// Creates the profile of a newly registered customer when Identity's <c>CustomerRegistered</c> event arrives
/// (BR-CUS-006). Delivery is at least once, so the handler is idempotent twice over: the inbox row is committed in the
/// same transaction as the profile (a redelivery finds it and does nothing), and an account that already has a profile
/// is left alone and still recorded as handled.
/// </summary>
/// <remarks>
/// Only the e-mail is essential. The name, phone, locale, and time zone are optional data the customer typed at
/// registration: one that breaks a rule must not cost the customer their profile, so it is left out (the customer
/// can set it with <c>PATCH /customers/me</c>) and reported in the outcome for the consumer to log, never its value.
/// </remarks>
internal sealed class CreateCustomerProfileOnRegistrationHandler(
    ICustomerProfileRepository profiles,
    IUnitOfWork unitOfWork,
    IInbox inbox,
    TimeProvider timeProvider
)
{
    /// <summary>Stable consumer name: the inbox key, the queue name and the telemetry label.</summary>
    public const string ConsumerName = "customers.create-profile-on-customer-registered";

    /// <summary>Executes the command.</summary>
    /// <param name="command">The event data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// How the message ended: <see cref="ProfileCreation.Duplicate"/> when it was handled before, otherwise a handled
    /// outcome listing the optional fields that were left out; or the violated rule when the e-mail is unusable.
    /// </returns>
    public async Task<Result<ProfileCreation>> HandleAsync(
        CreateCustomerProfileOnRegistrationCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AccountId == Guid.Empty)
        {
            return Result<ProfileCreation>.Failure(CustomerErrors.AccountInvalid);
        }

        var email = ContactEmail.Create(command.Email);
        if (email.IsFailure)
        {
            return Result<ProfileCreation>.Failure(email.Error);
        }

        if (!await inbox.TryBeginAsync(command.MessageId, ConsumerName, cancellationToken))
        {
            return Result<ProfileCreation>.Success(ProfileCreation.Duplicate);
        }

        var ignored = new List<string>();
        var fullName = Optional(command.FullName, FullName.Create, "fullName", ignored);
        var phone = Optional(command.Phone, PhoneNumber.Create, "phone", ignored);
        var locale = Optional(command.Locale, CustomerLocale.Create, "locale", ignored);
        var timeZone = Optional(command.TimeZone, CustomerTimeZone.Create, "timeZone", ignored);

        if (await profiles.GetByIdAsync(command.AccountId, cancellationToken) is null)
        {
            await profiles.AddAsync(
                CustomerProfile.Create(command.AccountId, email.Value, fullName, phone, locale, timeZone, timeProvider),
                cancellationToken
            );
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ProfileCreation>.Success(new ProfileCreation(Handled: true, ignored));
    }

    private static T? Optional<T>(string? raw, Func<string?, Result<T>> create, string field, List<string> ignored)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parsed = create(raw);
        if (parsed.IsSuccess)
        {
            return parsed.Value;
        }

        ignored.Add(field);
        return null;
    }
}
